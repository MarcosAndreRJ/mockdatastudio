using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MockDataStudio.Core.Common;

namespace MockDataStudio.Core.Templates;

public enum TemplateKind
{
    Scalar,
    Composite,
}

// Fonte de dados de templates (C05 §2). Implementada pelo Catálogo (CatalogTemplateDataSource); Generation não referencia Catalog (AR-02).
public interface ITemplateDataSource
{
    Task<TemplateDataSet> LoadAsync(string templateCode, CancellationToken ct);
}

// Conjunto imutável de itens de um template. Itens ordenados ordinalmente (escalar: Value; composto: RecordKey),
// de forma que a ordem de inserção não influencia o determinismo (T-CAT-009).
public sealed class TemplateDataSet
{
    private readonly string[] _values;
    private readonly string[][] _fields;
    private readonly double[] _weights;

    private TemplateDataSet(string templateCode, TemplateKind kind, IReadOnlyList<string> fieldCodes,
        string[] values, string[][] fields, double[] weights, string contentHash)
    {
        TemplateCode = templateCode;
        Kind = kind;
        FieldCodes = fieldCodes;
        _values = values;
        _fields = fields;
        _weights = weights;
        ContentHash = contentHash;
    }

    public string TemplateCode { get; }

    public TemplateKind Kind { get; }

    public string ContentHash { get; }

    public int Count => _weights.Length;

    // Vazio para template escalar.
    public IReadOnlyList<string> FieldCodes { get; }

    public bool HasNonUniformWeights => _weights.Distinct().Count() > 1;

    public string GetScalar(int index)
    {
        if (Kind != TemplateKind.Scalar)
        {
            throw new InvalidOperationException("Template composto não tem valor escalar.");
        }

        return _values[index];
    }

    public string? GetField(int index, string fieldCode)
    {
        if (Kind != TemplateKind.Composite)
        {
            throw new InvalidOperationException("Template escalar não tem campos.");
        }

        var position = FieldCodes.ToList().IndexOf(fieldCode);
        if (position < 0)
        {
            throw new ArgumentException($"Campo '{fieldCode}' inexistente no template '{TemplateCode}'.", nameof(fieldCode));
        }

        return _fields[index][position];
    }

    public double GetWeight(int index) => _weights[index];

    // Monta o conjunto a partir de itens já validados. Ordena e calcula o ContentHash sobre os itens ordenados e pesos.
    public static Result<TemplateDataSet> Create(string templateCode, TemplateKind kind, IReadOnlyList<string> fieldCodes,
        IEnumerable<(string Key, string[]? Fields, string? Scalar, double Weight)> items)
    {
        var ordered = items
            .OrderBy(i => i.Key, StringComparer.Ordinal)
            .ToList();

        if (ordered.Any(i => i.Weight <= 0 || double.IsNaN(i.Weight) || double.IsInfinity(i.Weight)))
        {
            return Result<TemplateDataSet>.Fail(ErrorCodes.CatImpWeight, "Peso deve ser positivo e finito.");
        }

        var values = ordered.Select(i => i.Scalar ?? string.Empty).ToArray();
        var fields = ordered.Select(i => i.Fields ?? []).ToArray();
        var weights = ordered.Select(i => i.Weight).ToArray();

        var canonical = new List<object>();
        for (var i = 0; i < ordered.Count; i++)
        {
            canonical.Add(kind == TemplateKind.Scalar
                ? new object[] { ordered[i].Key, weights[i] }
                : new object[] { ordered[i].Key, fields[i], weights[i] });
        }

        var json = JsonSerializer.Serialize(new { code = templateCode, kind = kind.ToString(), items = canonical });
        var hash = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

        return Result<TemplateDataSet>.Ok(new TemplateDataSet(templateCode, kind, fieldCodes, values, fields, weights, hash));
    }
}
