namespace MockDataStudio.Mapping;

// Snapshot imutável do conhecimento do catálogo (aliases, templates, regras), carregado uma vez por sessão (docs/14 §10).
// Índices: aliases por Compact, por ExpandedCompact e por CoreToken; templates por código normalizado.
public sealed class MappingKnowledge
{
    private readonly Dictionary<string, KnowledgeTemplate> _templates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, NormalizedName> _templateNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<IndexedAlias>> _byCompact = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<IndexedAlias>> _byExpanded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<IndexedAlias>> _byCoreToken = new(StringComparer.Ordinal);

    public MappingKnowledge(IEnumerable<KnowledgeTemplate> templates, IEnumerable<KnowledgeAlias> aliases,
        IEnumerable<KnowledgeRule> rules, INameNormalizer normalizer)
    {
        ArgumentNullException.ThrowIfNull(normalizer);
        Normalizer = normalizer;
        Rules = rules.ToList();

        foreach (var template in templates)
        {
            _templates[template.Code] = template;
            _templateNames[template.Code] = normalizer.Normalize(template.Code);
        }

        foreach (var alias in aliases)
        {
            var normalized = normalizer.Normalize(alias.Alias);
            var entry = new IndexedAlias(alias, normalized);
            Add(_byCompact, normalized.Compact, entry);
            Add(_byExpanded, normalized.ExpandedCompact, entry);
            foreach (var token in normalized.CoreTokens.Distinct(StringComparer.Ordinal))
            {
                Add(_byCoreToken, token, entry);
            }
        }
    }

    public INameNormalizer Normalizer { get; }

    public IReadOnlyList<KnowledgeRule> Rules { get; }

    public IReadOnlyCollection<KnowledgeTemplate> Templates => _templates.Values;

    public bool TryGetTemplate(string code, out KnowledgeTemplate template) => _templates.TryGetValue(code, out template!);

    // Nome normalizado do código do template (para ExactTemplateCode).
    public NormalizedName TemplateName(string code) => _templateNames[code];

    public IReadOnlyList<IndexedAlias> ByCompact(string compact) => Get(_byCompact, compact);

    public IReadOnlyList<IndexedAlias> ByExpanded(string expandedCompact) => Get(_byExpanded, expandedCompact);

    public IReadOnlyList<IndexedAlias> ByCoreToken(string token) => Get(_byCoreToken, token);

    public IEnumerable<IndexedAlias> AllAliases() => _byCompact.Values.SelectMany(v => v).Distinct();

    private static void Add(Dictionary<string, List<IndexedAlias>> index, string key, IndexedAlias entry)
    {
        if (!index.TryGetValue(key, out var list))
        {
            list = [];
            index[key] = list;
        }

        list.Add(entry);
    }

    private static List<IndexedAlias> Get(Dictionary<string, List<IndexedAlias>> index, string key) =>
        index.TryGetValue(key, out var list) ? list : [];
}

// Alias do catálogo com sua forma normalizada (14 §2: normalized_alias = Compact; índice guarda ExpandedCompact e CoreTokens).
public sealed record IndexedAlias(KnowledgeAlias Alias, NormalizedName Normalized);
