using System.Security.Cryptography;
using System.Text;
using MockDataStudio.Core.Common;

namespace MockDataStudio.Output;

public sealed record SegmentedSinkOptions(
    long MaxBytes,
    bool WriteBom = false,
    string LineEnding = "\n",
    // Cabeçalho do segmento k (1-based). O token "{n}" marca onde vai o total de segmentos, conhecido só ao final.
    Func<int, string>? Header = null,
    // Comandos que fecham um segmento que será seguido de outro (ex.: COMMIT;) e que abrem o próximo (ex.: BEGIN;).
    Func<IReadOnlyList<string>>? BeforeRollover = null,
    Func<IReadOnlyList<string>>? AfterRollover = null);

/// <summary>
/// Escrita de comandos SQL em segmentos (docs/18 §6): nunca divide um comando, grava em <c>.partial</c> e renomeia ao fechar
/// o segmento, e registra SHA-256 e tamanho de cada arquivo final. O total de segmentos no cabeçalho é gravado ao final.
/// </summary>
public sealed class SegmentedTextSink : ISegmentedTextSink
{
    private const string TotalToken = "{n}";
    private const string TotalPlaceholder = "???";
    private const int TotalWidth = 3;

    private readonly string _directory;
    private readonly string _baseName;
    private readonly string _extension;
    private readonly string _kind;
    private readonly SegmentedSinkOptions _options;
    private readonly UTF8Encoding _encoding = new(false);
    private readonly List<Segment> _segments = [];
    private readonly List<OutputArtifact> _completed = [];
    private readonly List<string> _warnings = [];

    private StreamWriter? _writer;
    private Segment? _current;
    private long _bytes;
    private int _statements;
    private int _atomicDepth;
    private bool _oversizeReported;
    private bool _finished;

    public SegmentedTextSink(string directory, string baseName, string extension, string kind, SegmentedSinkOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseName);
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "O tamanho máximo do segmento deve ser positivo.");
        }

        _directory = directory;
        _baseName = baseName;
        _extension = extension.StartsWith('.') ? extension : "." + extension;
        _kind = kind;
        _options = options;
    }

    public IReadOnlyList<OutputArtifact> Completed => _completed;

    /// <summary>Avisos de execução (ex.: OUT-SEGMENT-OVERSIZE quando um trecho atômico excedeu o limite).</summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Caminhos dos arquivos já criados (finais ou .partial), na ordem dos segmentos.</summary>
    public IReadOnlyList<string> SegmentPaths => _segments.Select(s => s.Closed ? s.FinalPath : s.PartialPath).ToList();

    /// <summary>Marca um trecho que não pode ser dividido entre segmentos (ex.: componente de ciclo com constraint adiada).</summary>
    public void BeginAtomic() => _atomicDepth++;

    public void EndAtomic()
    {
        if (_atomicDepth > 0)
        {
            _atomicDepth--;
        }
    }

    public async Task WriteStatementAsync(string statement, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ct.ThrowIfCancellationRequested();

        var text = EolConverter.Convert(statement, _options.LineEnding) + _options.LineEnding;
        var size = _encoding.GetByteCount(text);
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        if (_statements > 0 && _bytes + size > _options.MaxBytes)
        {
            if (_atomicDepth > 0)
            {
                if (!_oversizeReported)
                {
                    _warnings.Add($"{ErrorCodes.OutSegmentOversize}: segmento {_current!.Index} excede {_options.MaxBytes} bytes porque contém um trecho que não pode ser dividido.");
                    _oversizeReported = true;
                }
            }
            else
            {
                await RolloverAsync(ct).ConfigureAwait(false);
            }
        }

        await WriteRawAsync(text, ct).ConfigureAwait(false);
        _statements++;
    }

    public async Task WriteCommentAsync(string comment, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(comment);
        ct.ThrowIfCancellationRequested();
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        foreach (var line in comment.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var text = (line.StartsWith("--", StringComparison.Ordinal) ? line : "-- " + line) + _options.LineEnding;
            await WriteRawAsync(text, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Finaliza: fecha o último segmento, grava o total nos cabeçalhos e calcula SHA-256 e tamanho de cada arquivo.</summary>
    public async Task FinishAsync(CancellationToken ct)
    {
        if (_finished)
        {
            return;
        }

        await EnsureOpenAsync(ct).ConfigureAwait(false); // saída vazia ainda gera um segmento válido (só cabeçalho)
        await CloseCurrentAsync(finalize: true, ct).ConfigureAwait(false);
        _finished = true;

        var total = _segments.Count.ToString("D" + TotalWidth, System.Globalization.CultureInfo.InvariantCulture);
        foreach (var segment in _segments)
        {
            if (segment.TotalOffset >= 0)
            {
                await using var patch = new FileStream(segment.FinalPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                patch.Seek(segment.TotalOffset, SeekOrigin.Begin);
                await patch.WriteAsync(Encoding.ASCII.GetBytes(total), ct).ConfigureAwait(false);
            }

            await using var read = new FileStream(segment.FinalPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = await SHA256.HashDataAsync(read, ct).ConfigureAwait(false);
            _completed.Add(new OutputArtifact(segment.FinalPath, read.Length, Convert.ToHexString(hash).ToLowerInvariant(), segment.Index, _kind));
        }
    }

    /// <summary>Interrompe sem finalizar: o segmento corrente permanece como <c>.partial</c> para diagnóstico.</summary>
    public async Task AbortAsync()
    {
        if (_finished)
        {
            return;
        }

        await CloseCurrentAsync(finalize: false, CancellationToken.None).ConfigureAwait(false);
        _finished = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_finished)
        {
            await AbortAsync().ConfigureAwait(false);
        }
    }

    private async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (_writer is not null)
        {
            return;
        }

        var index = _segments.Count + 1;
        Directory.CreateDirectory(_directory);
        var name = index == 1 ? _baseName + _extension : $"{_baseName}_part{index:D3}{_extension}";
        var segment = new Segment(index, Path.Combine(_directory, name));
        _segments.Add(segment);
        _current = segment;

        var stream = new FileStream(segment.PartialPath, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20, FileOptions.Asynchronous);
        _writer = new StreamWriter(stream, _encoding, 1 << 20);
        _bytes = 0;
        _statements = 0;
        _oversizeReported = false;

        if (_options.WriteBom)
        {
            await stream.WriteAsync(Encoding.UTF8.GetPreamble(), ct).ConfigureAwait(false);
            _bytes += Encoding.UTF8.GetPreamble().Length;
        }

        if (_options.Header?.Invoke(index) is { Length: > 0 } header)
        {
            var normalized = EolConverter.Convert(header, _options.LineEnding);
            var marker = normalized.IndexOf(TotalToken, StringComparison.Ordinal);
            var text = marker < 0
                ? normalized
                : string.Concat(normalized.AsSpan(0, marker), TotalPlaceholder, normalized.AsSpan(marker + TotalToken.Length));
            if (marker >= 0)
            {
                segment.TotalOffset = _bytes + _encoding.GetByteCount(text.AsSpan(0, marker));
            }

            await WriteRawAsync(text + (text.EndsWith(_options.LineEnding, StringComparison.Ordinal) ? string.Empty : _options.LineEnding), ct)
                .ConfigureAwait(false);
        }
    }

    private async Task WriteRawAsync(string text, CancellationToken ct)
    {
        try
        {
            await _writer!.WriteAsync(text.AsMemory(), ct).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new OutputException(ErrorCodes.OutIo, "Falha ao gravar o arquivo de saída: " + ex.Message);
        }

        _bytes += _encoding.GetByteCount(text);
    }

    private async Task RolloverAsync(CancellationToken ct)
    {
        foreach (var statement in _options.BeforeRollover?.Invoke() ?? [])
        {
            await WriteRawAsync(EolConverter.Convert(statement, _options.LineEnding) + _options.LineEnding, ct).ConfigureAwait(false);
        }

        await CloseCurrentAsync(finalize: true, ct).ConfigureAwait(false);
        await EnsureOpenAsync(ct).ConfigureAwait(false);

        foreach (var statement in _options.AfterRollover?.Invoke() ?? [])
        {
            await WriteRawAsync(EolConverter.Convert(statement, _options.LineEnding) + _options.LineEnding, ct).ConfigureAwait(false);
        }
    }

    private async Task CloseCurrentAsync(bool finalize, CancellationToken ct)
    {
        if (_writer is null || _current is null)
        {
            return;
        }

        try
        {
            await _writer.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
            _writer = null;
        }

        if (finalize)
        {
            File.Move(_current.PartialPath, _current.FinalPath, overwrite: true);
            _current.Closed = true;
        }
    }

    private sealed class Segment(int index, string finalPath)
    {
        public int Index { get; } = index;

        public string FinalPath { get; } = finalPath;

        public string PartialPath { get; } = finalPath + ".partial";

        public long TotalOffset { get; set; } = -1;

        public bool Closed { get; set; }
    }
}

/// <summary>
/// Converte as quebras de linha estruturais de um comando SQL para a sequência desejada, sem tocar no conteúdo de literais
/// ('...') nem de identificadores ("..."): uma quebra de linha dentro de um valor faz parte do dado.
/// </summary>
public static class EolConverter
{
    public static string Convert(string sql, string lineEnding)
    {
        ArgumentNullException.ThrowIfNull(sql);
        if (lineEnding == "\n" || !sql.Contains('\n', StringComparison.Ordinal))
        {
            return sql;
        }

        var builder = new StringBuilder(sql.Length + 16);
        var quote = '\0';
        for (var i = 0; i < sql.Length; i++)
        {
            var c = sql[i];
            if (quote != '\0')
            {
                builder.Append(c);
                if (c == quote)
                {
                    quote = '\0'; // aspas duplicadas ('' ou "") reabrem no caractere seguinte
                }

                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                builder.Append(c);
            }
            else if (c == '\n' && (i == 0 || sql[i - 1] != '\r'))
            {
                builder.Append(lineEnding);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
