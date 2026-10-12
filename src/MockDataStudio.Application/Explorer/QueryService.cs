using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using MockDataStudio.Core.Common;
using MockDataStudio.Providers;
using MockDataStudio.SqlTooling.Classification;
using MockDataStudio.SqlTooling.Dialect;
using MockDataStudio.SqlTooling.Splitter;

namespace MockDataStudio.Application.Explorer;

// Serviço de execução compartilhado pelo Explorer e pelo editor SQL independente (prompt, seção 16).
// Fluxo: dividir o script (SqlTooling) → avaliar política por comando → aplicar restrição de sessão → executar
// com cancelamento e timeout → devolver resultados paginados (ResultCursor) e mensagens estruturadas.

public enum MessageSeverity { Info, Warning, Error }

public sealed record ExecutionMessage(MessageSeverity Severity, string Text);

public enum StatementStatus { Succeeded, Failed, Blocked, RequiresConfirmation, Cancelled }

public sealed record ResultColumnInfo(string Name, string NativeType);

public sealed record ExecutionRequest(
    string Script,
    SqlDialectFeatures Dialect,
    ExecutionPolicy Policy,
    bool Confirmed = false,
    bool AuthorizedProduction = false,
    int PageSize = 500,
    int CommandTimeoutSec = 0);

public sealed class StatementOutcome : IAsyncDisposable
{
    public required int Index { get; init; }

    public required string Text { get; init; }

    // Linha (1-based) em que o comando começa no script; base para o destaque de erro no editor.
    public required int Line { get; init; }

    public required StatementKind Kind { get; init; }

    public required StatementStatus Status { get; init; }

    public IReadOnlyList<ResultColumnInfo> Columns { get; init; } = [];

    public IReadOnlyList<object?[]> FirstPage { get; init; } = [];

    public bool HasMore { get; init; }

    // Cursor da grade (paginação). Nulo quando o comando não retorna linhas. Deve ser descartado pelo chamador.
    public ResultCursor? Cursor { get; init; }

    public long? RecordsAffected { get; init; }

    public TimeSpan Duration { get; init; }

    public IReadOnlyList<ExecutionMessage> Messages { get; init; } = [];

    public MdsError? Error { get; init; }

    public async ValueTask DisposeAsync()
    {
        if (Cursor is not null)
        {
            await Cursor.DisposeAsync();
        }
    }
}

public sealed class QueryRun : IAsyncDisposable
{
    public required IReadOnlyList<StatementOutcome> Statements { get; init; }

    public IReadOnlyList<ExecutionMessage> Messages { get; init; } = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var statement in Statements)
        {
            await statement.DisposeAsync();
        }
    }
}

public sealed class QueryService(ISqlScriptSplitter splitter, IErrorTranslator errors)
{
    public async Task<QueryRun> ExecuteAsync(DbConnection connection, ExecutionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(request);

        var notes = new List<ExecutionMessage>();
        var outcomes = new List<StatementOutcome>();

        if (request.Policy.ProfileReadOnly && !await ApplyReadOnlySessionAsync(connection, request.Policy, notes, ct))
        {
            // Sem restrição efetiva, nada é executado: perfil somente leitura não é protegido só por configuração visual.
            return new QueryRun { Statements = [], Messages = notes };
        }

        var statements = splitter.Split(request.Script, request.Dialect);
        for (var i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            var access = ExecutionPolicy.AccessOf(statement.Kind);
            var verdict = request.Policy.Evaluate(access, statement.IsDestructive, request.AuthorizedProduction);

            if (verdict.Decision == AccessDecision.Blocked)
            {
                outcomes.Add(Done(i, statement, StatementStatus.Blocked, TimeSpan.Zero, null,
                    [new ExecutionMessage(MessageSeverity.Error, verdict.Reason)]));
                break;
            }

            if (verdict.Decision == AccessDecision.RequiresConfirmation && !request.Confirmed)
            {
                outcomes.Add(Done(i, statement, StatementStatus.RequiresConfirmation, TimeSpan.Zero, null,
                    [new ExecutionMessage(MessageSeverity.Warning, verdict.Reason)]));
                break;
            }

            var outcome = await ExecuteStatementAsync(connection, i, statement, request, ct);
            outcomes.Add(outcome);
            if (outcome.Status is StatementStatus.Failed or StatementStatus.Cancelled)
            {
                break;
            }
        }

        return new QueryRun { Statements = outcomes, Messages = notes };
    }

    // Restrição efetiva de sessão: quando o provider a oferece, o próprio banco rejeita escritas (defesa em profundidade).
    private static async Task<bool> ApplyReadOnlySessionAsync(DbConnection connection, ExecutionPolicy policy, List<ExecutionMessage> notes, CancellationToken ct)
    {
        if (policy.SessionRestriction is null)
        {
            notes.Add(new ExecutionMessage(MessageSeverity.Error,
                "Perfil somente leitura sem restrição efetiva de sessão neste provider: execução bloqueada."));
            return false;
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = policy.SessionRestriction;
            await command.ExecuteNonQueryAsync(ct);
            return true;
        }
        catch (DbException ex)
        {
            notes.Add(new ExecutionMessage(MessageSeverity.Error, "Não foi possível aplicar a restrição somente leitura: " + ex.Message));
            return false;
        }
    }

    private async Task<StatementOutcome> ExecuteStatementAsync(DbConnection connection, int index, SqlCommandStatement statement,
        ExecutionRequest request, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        DbCommand? command = null;
        CancellationTokenRegistration? registration = null;
        DbDataReader? reader = null;
        var handedOff = false;
        try
        {
            command = connection.CreateCommand();
            command.CommandText = statement.Text;
            command.CommandTimeout = request.CommandTimeoutSec;

            // Cancelamento efetivo: o token chega ao motor (DbCommand.Cancel), inclusive durante a execução da consulta.
            registration = ct.Register(static state => ((DbCommand)state!).Cancel(), command);

            reader = await command.ExecuteReaderAsync(ct);

            if (reader.FieldCount == 0)
            {
                var affected = reader.RecordsAffected;
                clock.Stop();
                return Done(index, statement, StatementStatus.Succeeded, clock.Elapsed,
                    affected >= 0 ? affected : null,
                    [new ExecutionMessage(MessageSeverity.Info, $"Comando concluído: {Affected(affected)} em {Ms(clock.Elapsed)}.")]);
            }

            var columns = Enumerable.Range(0, reader.FieldCount)
                .Select(i => new ResultColumnInfo(reader.GetName(i), reader.GetDataTypeName(i)))
                .ToList();

            // Propriedade do leitor, comando e registro de cancelamento passa ao cursor.
            var cursor = new ResultCursor(command, reader, columns, registration);
            handedOff = true;
            reader = null;
            command = null;
            registration = null;

            ResultPage page;
            try
            {
                page = await cursor.NextPageAsync(request.PageSize, ct);
            }
            catch
            {
                await cursor.DisposeAsync();
                throw;
            }

            clock.Stop();
            var more = page.HasMore ? " (há mais linhas no banco)" : string.Empty;
            return new StatementOutcome
            {
                Index = index,
                Text = statement.Text,
                Line = statement.LineNumber,
                Kind = statement.Kind,
                Status = StatementStatus.Succeeded,
                Columns = columns,
                FirstPage = page.Rows,
                HasMore = page.HasMore,
                Cursor = cursor,
                Duration = clock.Elapsed,
                Messages = [new ExecutionMessage(MessageSeverity.Info,
                    $"Consulta concluída: {page.Rows.Count} linhas exibidas{more} em {Ms(clock.Elapsed)}.")],
            };
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            clock.Stop();
            return Done(index, statement, StatementStatus.Cancelled, clock.Elapsed, null,
                [new ExecutionMessage(MessageSeverity.Warning, "Execução cancelada pelo usuário.")]);
        }
        catch (Exception ex) when (ex is DbException or InvalidOperationException or FormatException)
        {
            clock.Stop();
            var error = errors.Translate(ex);
            return new StatementOutcome
            {
                Index = index,
                Text = statement.Text,
                Line = statement.LineNumber,
                Kind = statement.Kind,
                Status = StatementStatus.Failed,
                Duration = clock.Elapsed,
                Error = error,
                Messages = [new ExecutionMessage(MessageSeverity.Error,
                    $"Erro na linha {statement.LineNumber}: {error.Message}")],
            };
        }
        finally
        {
            if (!handedOff)
            {
                registration?.Dispose();
                reader?.Dispose();
                command?.Dispose();
            }
        }
    }

    private static StatementOutcome Done(int index, SqlCommandStatement statement, StatementStatus status, TimeSpan duration,
        long? affected, IReadOnlyList<ExecutionMessage> messages) => new()
    {
        Index = index,
        Text = statement.Text,
        Line = statement.LineNumber,
        Kind = statement.Kind,
        Status = status,
        Duration = duration,
        RecordsAffected = affected,
        Messages = messages,
    };

    private static string Affected(long affected) =>
        affected >= 0 ? string.Create(CultureInfo.InvariantCulture, $"{affected} linha(s) afetada(s)") : "número de linhas afetadas não informado";

    private static string Ms(TimeSpan elapsed) =>
        string.Create(CultureInfo.InvariantCulture, $"{elapsed.TotalMilliseconds:0} ms");
}

// Cursor de páginas sobre o leitor do banco. Mantém um comando aberto até ser descartado.
public sealed class ResultCursor(DbCommand command, DbDataReader reader, IReadOnlyList<ResultColumnInfo> columns,
    CancellationTokenRegistration? cancellation = null) : IAsyncDisposable
{
    private object?[]? _pending;
    private bool _exhausted;
    private bool _disposed;

    public IReadOnlyList<ResultColumnInfo> Columns { get; } = columns;

    public long RowsRead { get; private set; }

    public async Task<ResultPage> NextPageAsync(int size, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        var rows = new List<object?[]>(Math.Min(size, 1000));
        while (rows.Count < size)
        {
            var row = await NextRowAsync(ct);
            if (row is null)
            {
                break;
            }

            rows.Add(row);
        }

        // Lookahead de uma linha: sabe-se se há mais páginas sem carregar o conjunto inteiro.
        if (_pending is null && !_exhausted)
        {
            _pending = await NextRowAsync(ct);
        }

        RowsRead += rows.Count;
        return new ResultPage(rows, _pending is not null);
    }

    private async Task<object?[]?> NextRowAsync(CancellationToken ct)
    {
        if (_pending is { } buffered)
        {
            _pending = null;
            return buffered;
        }

        if (_exhausted || !await reader.ReadAsync(ct))
        {
            _exhausted = true;
            return null;
        }

        var row = new object?[reader.FieldCount];
        for (var i = 0; i < row.Length; i++)
        {
            row[i] = await reader.IsDBNullAsync(i, ct) ? null : reader.GetValue(i);
        }

        return row;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (cancellation is { } registration)
        {
            await registration.DisposeAsync();
        }
        await reader.DisposeAsync();
        await command.DisposeAsync();
    }
}

public sealed record ResultPage(IReadOnlyList<object?[]> Rows, bool HasMore);
