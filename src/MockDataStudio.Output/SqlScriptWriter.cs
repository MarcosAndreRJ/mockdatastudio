using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MockDataStudio.Core.Common;
using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Core.Schema;
using MockDataStudio.Generation.Engine;
using MockDataStudio.Generation.Planning;
using MockDataStudio.Providers;
using MockDataStudio.Providers.Models;
using ExecTable = MockDataStudio.Generation.Engine.TablePlan;
using PlanTable = MockDataStudio.Generation.Planning.TablePlan;

namespace MockDataStudio.Output;

/// <summary>
/// Writer de script SQL (docs/18): cabeçalho, preâmbulo de sessão, transações com commits periódicos, INSERTs pelo dialeto,
/// UPDATEs adiados, ressincronização de sequences, guardas de faixa de chave, segmentação, manifesto e script de limpeza.
/// Nenhum dado sensível (senhas, connection strings, host) entra no arquivo.
/// </summary>
public sealed class SqlScriptWriter(OutputWriterOptions options) : IOutputWriter
{
    // Tuplas de chave guardadas para a limpeza de tabelas sem faixa inteira; acima disso a limpeza dessa tabela é manual.
    public const int MaxTrackedKeyTuples = 2_000_000;

    private static readonly JsonSerializerOptions ManifestJson = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly List<TableRunResult> _results = [];
    private readonly Dictionary<ObjectName, List<object?[]>> _trackedKeys = [];
    private readonly HashSet<ObjectName> _untrackable = [];

    private OutputContext? _ctx;
    private ISqlDialect? _dialect;
    private SegmentedTextSink? _sink;
    private string _baseName = string.Empty;
    private string _directory = string.Empty;
    private long _rowsSinceCommit;
    private long _rowsWritten;
    private bool _usesDisableChecks;
    private bool _atomic;
    private bool _completed;
    private bool _txOpen;
    private string? _overrideClause;
    private IReadOnlyList<ColumnModel> _currentColumns = [];
    private TableModel? _currentModel;
    private PlanTable? _currentPlan;

    public OutputKind Kind => OutputKind.SqlScript;

    public async Task BeginAsync(OutputContext ctx, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        _ctx = ctx;
        _dialect = ctx.Dialect ?? options.Dialect
            ?? throw new OutputException(ErrorCodes.OutIo, "O writer de script exige o dialeto SQL do destino.");

        var settings = ctx.Settings;
        _directory = string.IsNullOrWhiteSpace(settings.Directory) ? "out" : settings.Directory;
        _baseName = ExpandFileName(settings.FileNamePattern, ctx);

        var sinkOptions = new SegmentedSinkOptions(
            settings.SegmentMaxBytes, settings.WriteBom, settings.LineEnding,
            Header: SegmentHeader,
            BeforeRollover: () => settings.IncludeTransaction && _txOpen ? [_dialect.Commit] : [],
            AfterRollover: () =>
            {
                _rowsSinceCommit = 0;
                _txOpen = settings.IncludeTransaction && _dialect.BeginTransaction is not null;
                return _txOpen ? [_dialect.BeginTransaction!] : [];
            });
        _sink = new SegmentedTextSink(_directory, _baseName, ".sql", "script", sinkOptions);

        _usesDisableChecks = ctx.Plan.Tables.Values.Any(t => t.DeferredFks.Any(d => d.Strategy == CycleBreakStrategy.DisableChecks));
        _atomic = ctx.Plan.Tables.Values.Any(t => t.DeferredFks.Any(d => d.Strategy == CycleBreakStrategy.DeferredConstraint));

        // Preâmbulo de sessão e abertura da transação (segmento 1).
        var scriptOptions = new ScriptOptions(settings.Encoding, settings.QuoteAllIdentifiers, settings.IncludeTransaction);
        foreach (var line in _dialect.SessionPreamble(scriptOptions))
        {
            await _sink.WriteStatementAsync(line, ct).ConfigureAwait(false);
        }

        if (settings.IncludeTransaction && _dialect.BeginTransaction is { } begin)
        {
            await _sink.WriteStatementAsync(begin, ct).ConfigureAwait(false);
            _txOpen = true;
        }

        if (_atomic)
        {
            _sink.BeginAtomic(); // constraints adiadas valem por transação: o script inteiro fica num só segmento (DA-1xx)
            foreach (var line in _dialect.DeferConstraintStatements())
            {
                await _sink.WriteStatementAsync(line, ct).ConfigureAwait(false);
            }
        }

        if (_usesDisableChecks && _dialect.DisableFkChecks() is { } disable)
        {
            await _sink.WriteStatementAsync(disable, ct).ConfigureAwait(false);
        }

        if (settings.EmitGuards)
        {
            await WriteGuardsAsync(ct).ConfigureAwait(false);
        }
    }

    public async Task ExecuteSessionStepAsync(PlanStep step, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(step);
        var (ctx, dialect, sink) = Require();

        switch (step)
        {
            case SessionSetupStep setup:
                foreach (var statement in setup.Statements)
                {
                    await sink.WriteStatementAsync(statement, ct).ConfigureAwait(false);
                }

                break;

            case SessionTeardownStep teardown:
                foreach (var statement in teardown.Statements)
                {
                    await sink.WriteStatementAsync(statement, ct).ConfigureAwait(false);
                }

                break;

            case ResyncSequenceStep resync when ctx.Settings.ResyncSequences:
                await WriteResyncAsync(resync, ctx, dialect, sink, ct).ConfigureAwait(false);
                break;
        }
    }

    public async Task BeginTableAsync(ExecTable table, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(table);
        var (ctx, dialect, sink) = Require();

        _currentModel = ctx.Schema.FindTable(table.Table);
        _currentColumns = _currentModel?.Columns ?? [];
        _currentPlan = ctx.Plan.Tables.GetValueOrDefault(table.Table);
        _overrideClause = null;
        if (_currentPlan?.Keys.FirstOrDefault(k => k.NeedsIdentityOverride) is { } key && _currentModel?.FindColumn(key.Column) is { } column)
        {
            _overrideClause = dialect.IdentityOverrideClause(column);
        }

        await sink.WriteCommentAsync($"Tabela: {Display(table.Table)} ({table.RowCount.ToString(CultureInfo.InvariantCulture)} linhas)", ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<object?[]>?> WriteBatchAsync(RowBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var (ctx, dialect, sink) = Require();
        if (batch.Rows.Length == 0)
        {
            return null;
        }

        var types = batch.Columns.Select(name => TypeOf(batch.Table, name, ctx.Schema)).ToList();
        var perStatement = ctx.Settings.RowsPerStatement > 0 ? ctx.Settings.RowsPerStatement : Math.Max(1, dialect.RecommendedRowsPerInsert);
        TrackKeys(batch, ctx.Schema);

        for (var offset = 0; offset < batch.Rows.Length; offset += perStatement)
        {
            var count = Math.Min(perStatement, batch.Rows.Length - offset);
            var literalRows = new List<IReadOnlyList<string>>(count);
            for (var r = 0; r < count; r++)
            {
                var row = batch.Rows[offset + r];
                var literals = new string[row.Length];
                for (var c = 0; c < row.Length; c++)
                {
                    literals[c] = FormatLiteral(dialect, row[c], types[c], batch.Table, batch.Columns[c], batch.FirstRowIndex + offset + r);
                }

                literalRows.Add(literals);
            }

            await sink.WriteStatementAsync(dialect.BuildInsert(batch.Table, batch.Columns, literalRows, _overrideClause), ct).ConfigureAwait(false);
            _rowsWritten += count;
            if (dialect.BeginTransaction is null)
            {
                _txOpen = true; // sem BEGIN explícito (Firebird), o primeiro comando abre a transação
            }

            await CommitIfDueAsync(count, ctx, dialect, sink, ct).ConfigureAwait(false);
        }

        return null;
    }

    public Task EndTableAsync(TableRunResult result, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);
        var ranges = result.KeyRanges.Count > 0 ? result.KeyRanges : DerivedKeyRanges(result);
        _results.Add(result with { KeyRanges = ranges });
        return Task.CompletedTask;
    }

    public async Task WriteDeferredUpdatesAsync(DeferredUpdateBatch batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);
        var (ctx, dialect, sink) = Require();
        var keyTypes = batch.KeyColumns.Select(c => TypeOf(batch.Table, c, ctx.Schema)).ToList();
        var fkTypes = batch.FkColumns.Select(c => TypeOf(batch.Table, c, ctx.Schema)).ToList();

        for (var i = 0; i < batch.KeyValues.Length; i++)
        {
            var set = string.Join(", ", batch.FkColumns.Select((c, k) =>
                dialect.QuoteIdentifier(c) + " = " + FormatLiteral(dialect, batch.FkValues[i][k], fkTypes[k], batch.Table, c, i)));
            var where = string.Join(" AND ", batch.KeyColumns.Select((c, k) =>
                dialect.QuoteIdentifier(c) + " = " + FormatLiteral(dialect, batch.KeyValues[i][k], keyTypes[k], batch.Table, c, i)));
            await sink.WriteStatementAsync($"UPDATE {dialect.QualifiedName(batch.Table)} SET {set} WHERE {where};", ct).ConfigureAwait(false);
            await CommitIfDueAsync(1, ctx, dialect, sink, ct).ConfigureAwait(false);
        }
    }

    public async Task<OutputResult> CompleteAsync(RunStatus status, CancellationToken ct)
    {
        var (ctx, dialect, sink) = Require();
        if (_completed)
        {
            throw new InvalidOperationException("A saída já foi concluída.");
        }

        _completed = true;
        var artifacts = new List<OutputArtifact>();

        if (status == RunStatus.Completed)
        {
            if (_usesDisableChecks && dialect.EnableFkChecks() is { } enable)
            {
                await sink.WriteStatementAsync(enable, ct).ConfigureAwait(false);
            }

            if (_atomic)
            {
                sink.EndAtomic();
            }

            if (ctx.Settings.IncludeTransaction)
            {
                await sink.WriteStatementAsync(dialect.Commit, ct).ConfigureAwait(false);
            }

            await sink.FinishAsync(ct).ConfigureAwait(false);
            artifacts.AddRange(sink.Completed);

            if (ctx.Settings.IncludeCleanupScript)
            {
                artifacts.Add(await WriteCleanupAsync(ctx, dialect, ct).ConfigureAwait(false));
            }
        }
        else
        {
            await sink.AbortAsync().ConfigureAwait(false);
            var index = 1;
            foreach (var path in sink.SegmentPaths)
            {
                var length = File.Exists(path) ? new FileInfo(path).Length : 0;
                artifacts.Add(new OutputArtifact(path, length, string.Empty, index++, "script"));
            }
        }

        var manifestPath = Path.Combine(_directory, _baseName + ".manifest.json");
        var manifest = new RunManifest(
            "mds.run-manifest", "1.0", ctx.RunId, status.ToString().ToLowerInvariant(), ctx.Header,
            artifacts.Where(a => a.Kind == "script").ToList(),
            _results.Select(r => r with { KeyRanges = r.KeyRanges.Select(k => k with { CapturedKeys = null }).ToList() }).ToList(), // tuplas capturadas só servem à limpeza
            "Execute os segmentos de script em ordem crescente de índice. O script de limpeza é independente e não deve ser executado junto.");
        await WriteAtomicAsync(manifestPath, JsonSerializer.Serialize(manifest, ManifestJson), ct).ConfigureAwait(false);
        var manifestInfo = new FileInfo(manifestPath);
        artifacts.Add(new OutputArtifact(manifestPath, manifestInfo.Length, await Sha256Async(manifestPath, ct).ConfigureAwait(false), 0, "manifest"));

        return new OutputResult(artifacts, _rowsWritten, manifestPath);
    }

    public async ValueTask DisposeAsync()
    {
        if (_sink is not null)
        {
            await _sink.DisposeAsync().ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------- cabeçalho, guardas e sessão

    private static readonly string Rule = "-- " + new string('=', 60);

    private string SegmentHeader(int segment)
    {
        var (ctx, _, _) = Require();
        var h = ctx.Header;
        var lines = new List<string> { Rule };
        if (segment == 1)
        {
            lines.Add($"-- Mock Data Studio {h.ToolVersion} — script gerado");
            lines.Add($"-- Projeto: {Sanitize(ctx.ProjectName)}  Perfil: {Sanitize(ctx.ProfileName)}  RunId: {ctx.RunId}");
            lines.Add($"-- Destino: {h.TargetEngine}{(h.TargetServerVersion is null ? string.Empty : " " + h.TargetServerVersion)}   Gerado em: {Utc(h.GeneratedAt)}");
            lines.Add($"-- Seed: {h.Seed.ToString(CultureInfo.InvariantCulture)}  ReferenceInstant: {Offset(h.ReferenceInstant)}");
            lines.Add($"-- PlanHash: {h.PlanHash}");
            lines.Add($"-- StructureHash: {h.StructureHash}");
            lines.Add($"-- Reprodutibilidade: {h.Reproducibility}");
        }
        else
        {
            lines.Add($"-- Mock Data Studio {h.ToolVersion} — continuação do script (RunId: {ctx.RunId})");
        }

        lines.Add($"-- Segmento {segment.ToString("D3", CultureInfo.InvariantCulture)} de {{n}} — executar em ordem (ver manifesto)");
        if (segment == 1 && KeyRangeSummary(ctx) is { Length: > 0 } ranges)
        {
            lines.Add("-- Faixas de chave reservadas: " + ranges);
        }

        lines.Add(Rule);
        return string.Join('\n', lines);
    }

    private static string KeyRangeSummary(OutputContext ctx)
    {
        var parts = new List<string>();
        foreach (var table in OrderedTables(ctx.Plan))
        {
            foreach (var key in table.Keys.Where(k => k.Strategy == KeyStrategy.Explicit && k.Start is not null))
            {
                if (table.RowCount == 0)
                {
                    continue;
                }

                var last = key.Start!.Value + (table.RowCount - 1) * key.Step;
                parts.Add($"{table.Table.Name}.{key.Column} [{key.Start.Value.ToString(CultureInfo.InvariantCulture)}..{last.ToString(CultureInfo.InvariantCulture)}]");
            }
        }

        return string.Join(" ", parts);
    }

    private async Task WriteGuardsAsync(CancellationToken ct)
    {
        var (ctx, dialect, sink) = Require();
        foreach (var table in OrderedTables(ctx.Plan))
        {
            foreach (var key in table.Keys.Where(k => k.Strategy == KeyStrategy.Explicit && k.Start is not null))
            {
                if (table.RowCount == 0)
                {
                    continue;
                }

                var first = key.Start!.Value;
                var last = first + (table.RowCount - 1) * key.Step;
                var guard = dialect.BuildKeyRangeGuard(table.Table, key.Column, first, last);
                if (guard is not null)
                {
                    await sink.WriteStatementAsync(guard, ct).ConfigureAwait(false);
                }
                else
                {
                    await sink.WriteCommentAsync(
                        $"VERIFICAÇÃO: SELECT MAX({key.Column}) FROM {Display(table.Table)} deve ser menor que {first.ToString(CultureInfo.InvariantCulture)} (faixa [{first.ToString(CultureInfo.InvariantCulture)}..{last.ToString(CultureInfo.InvariantCulture)}] reservada).", ct)
                        .ConfigureAwait(false);
                }
            }
        }
    }


    private static async Task WriteResyncAsync(ResyncSequenceStep step, OutputContext ctx, ISqlDialect dialect, SegmentedTextSink sink, CancellationToken ct)
    {
        var plan = ctx.Plan.Tables.GetValueOrDefault(step.Table);
        var key = plan?.Keys.FirstOrDefault(k => k.Column == step.Column);
        var column = ctx.Schema.FindTable(step.Table)?.FindColumn(step.Column);
        if (plan is null || key?.Start is not { } start || column is null)
        {
            await sink.WriteCommentAsync($"Ressincronização de {Display(step.Table)}.{step.Column}: faixa de chave desconhecida, execute manualmente.", ct).ConfigureAwait(false);
            return;
        }

        var next = start + plan.RowCount * key.Step;
        var statements = dialect.ResyncSequenceStatements(step.Table, column, step.Sequence, next);
        if (statements.Count == 0)
        {
            await sink.WriteCommentAsync($"Ressincronização: não necessária para {Display(step.Table)}.{step.Column}", ct).ConfigureAwait(false);
            return;
        }

        foreach (var statement in statements)
        {
            await sink.WriteStatementAsync(statement, ct).ConfigureAwait(false);
        }
    }

    // ---------------------------------------------------------------- commits, literais e chaves

    private async Task CommitIfDueAsync(long rows, OutputContext ctx, ISqlDialect dialect, SegmentedTextSink sink, CancellationToken ct)
    {
        _rowsSinceCommit += rows;
        var every = options.CommitEveryRows ?? dialect.RecommendedCommitRows;
        if (_atomic || !ctx.Settings.IncludeTransaction || _rowsSinceCommit < every)
        {
            return;
        }

        await sink.WriteStatementAsync(dialect.Commit, ct).ConfigureAwait(false);
        if (dialect.BeginTransaction is { } begin)
        {
            await sink.WriteStatementAsync(begin, ct).ConfigureAwait(false);
        }

        _rowsSinceCommit = 0;
        _txOpen = dialect.BeginTransaction is not null;
    }

    private static string FormatLiteral(ISqlDialect dialect, object? value, DataTypeInfo type, ObjectName table, string column, long row)
    {
        try
        {
            return dialect.Literals.Format(value, type);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException or InvalidCastException)
        {
            throw new OutputException("OUT-LITERAL",
                $"Valor não representável como literal ({table.CanonicalId}.{column}, linha {row}): {ex.Message}");
        }
    }

    private static DataTypeInfo TypeOf(ObjectName table, string column, SchemaModel schema) =>
        schema.FindTable(table)?.FindColumn(column)?.Type
        ?? throw new OutputException(ErrorCodes.OutIo, $"Coluna '{column}' não existe na tabela {table.CanonicalId} do schema.");

    // Guarda as tuplas de chave primária das tabelas sem faixa inteira Explicit, para o script de limpeza.
    private void TrackKeys(RowBatch batch, SchemaModel schema)
    {
        var plan = _ctx!.Plan.Tables.GetValueOrDefault(batch.Table);
        if (plan is null || plan.Keys.Any(k => k.Strategy == KeyStrategy.Explicit && k.Start is not null) || _untrackable.Contains(batch.Table))
        {
            return;
        }

        if (schema.FindTable(batch.Table)?.PrimaryKey is not { Columns.Count: > 0 } pk)
        {
            return;
        }

        var indexes = pk.Columns.Select(c => batch.Columns.ToList().IndexOf(c)).ToList();
        if (indexes.Any(i => i < 0))
        {
            return; // chave não gerada pelo motor (ex.: DatabaseDefault): só a inserção direta captura
        }

        if (!_trackedKeys.TryGetValue(batch.Table, out var list))
        {
            _trackedKeys[batch.Table] = list = [];
        }

        if (list.Count + batch.Rows.Length > MaxTrackedKeyTuples)
        {
            _untrackable.Add(batch.Table);
            _trackedKeys.Remove(batch.Table);
            return;
        }

        foreach (var row in batch.Rows)
        {
            list.Add(indexes.Select(i => row[i]).ToArray());
        }
    }

    private List<KeyRangeResult> DerivedKeyRanges(TableRunResult result)
    {
        var ranges = new List<KeyRangeResult>();
        var plan = _ctx!.Plan.Tables.GetValueOrDefault(result.Table);
        foreach (var key in plan?.Keys.Where(k => k.Strategy == KeyStrategy.Explicit && k.Start is not null) ?? [])
        {
            var last = result.RowsWritten == 0 ? (long?)null : key.Start!.Value + (result.RowsWritten - 1) * key.Step;
            ranges.Add(new KeyRangeResult(key.Column, result.RowsWritten == 0 ? null : key.Start, last));
        }

        if (ranges.Count == 0 && _trackedKeys.TryGetValue(result.Table, out var tuples) && _ctx.Schema.FindTable(result.Table)?.PrimaryKey is { } pk)
        {
            ranges.Add(new KeyRangeResult(string.Join(",", pk.Columns), null, null, tuples));
        }

        return ranges;
    }

    // ---------------------------------------------------------------- limpeza, manifesto e arquivos

    private async Task<OutputArtifact> WriteCleanupAsync(OutputContext ctx, ISqlDialect dialect, CancellationToken ct)
    {
        var settings = ctx.Settings;
        var sinkOptions = new SegmentedSinkOptions(long.MaxValue, settings.WriteBom, settings.LineEnding,
            Header: _ => $"-- Mock Data Studio {ctx.Header.ToolVersion} — limpeza da execução {ctx.RunId}\n-- Segmento {{n}}");
        await using var sink = new SegmentedTextSink(_directory, _baseName + "_cleanup", ".sql", "cleanup", sinkOptions);

        var statements = new CleanupScriptBuilder(ctx.Schema).Build(ctx.Plan, _results, dialect);
        if (settings.IncludeTransaction && dialect.BeginTransaction is { } begin)
        {
            await sink.WriteStatementAsync(begin, ct).ConfigureAwait(false);
        }

        foreach (var statement in statements)
        {
            if (statement.StartsWith("--", StringComparison.Ordinal))
            {
                await sink.WriteCommentAsync(statement, ct).ConfigureAwait(false);
            }
            else
            {
                await sink.WriteStatementAsync(statement, ct).ConfigureAwait(false);
            }
        }

        if (settings.IncludeTransaction)
        {
            await sink.WriteStatementAsync(dialect.Commit, ct).ConfigureAwait(false);
        }

        await sink.FinishAsync(ct).ConfigureAwait(false);
        return sink.Completed[0];
    }

    private static async Task WriteAtomicAsync(string path, string content, CancellationToken ct)
    {
        var temp = path + ".partial";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(temp, content, new UTF8Encoding(false), ct).ConfigureAwait(false);
        File.Move(temp, path, overwrite: true);
    }

    private static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, ct).ConfigureAwait(false)).ToLowerInvariant();
    }

    private (OutputContext Ctx, ISqlDialect Dialect, SegmentedTextSink Sink) Require() =>
        _ctx is not null && _dialect is not null && _sink is not null
            ? (_ctx, _dialect, _sink)
            : throw new InvalidOperationException("BeginAsync deve ser chamado antes de escrever.");

    private static IEnumerable<PlanTable> OrderedTables(GenerationPlan plan)
    {
        foreach (var step in plan.Steps.OfType<InsertTableStep>())
        {
            if (plan.Tables.TryGetValue(step.Table, out var table))
            {
                yield return table;
            }
        }
    }

    private static string ExpandFileName(string? pattern, OutputContext ctx)
    {
        var text = string.IsNullOrWhiteSpace(pattern) ? "{project}_{profile}_{yyyyMMdd_HHmmss}" : pattern;
        text = text.Replace("{project}", ctx.ProjectName, StringComparison.Ordinal)
            .Replace("{profile}", ctx.ProfileName, StringComparison.Ordinal)
            .Replace("{yyyyMMdd_HHmmss}", ctx.Header.GeneratedAt.ToUniversalTime().ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(text.Select(c => invalid.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return cleaned.Length == 0 ? "script" : cleaned;
    }

    // Quebras de linha em nomes de projeto/perfil quebrariam o comentário de cabeçalho.
    private static string Sanitize(string text) => text.Replace('\r', ' ').Replace('\n', ' ');

    private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    private static string Offset(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string Display(ObjectName table) =>
        string.IsNullOrEmpty(table.Namespace) || table.Namespace == "main" ? table.Name : table.Namespace + "." + table.Name;
}

/// <summary>Fábrica do writer de script SQL.</summary>
public sealed class SqlScriptWriterFactory : IOutputWriterFactory
{
    public OutputKind Kind => OutputKind.SqlScript;

    public IOutputWriter Create(OutputWriterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new SqlScriptWriter(options);
    }
}
