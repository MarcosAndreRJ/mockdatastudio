using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Profiles;
using MockDataStudio.Core.Schema;

namespace MockDataStudio.Generation.Planning;

// Aresta filho → pai de uma FK aplicada cuja origem é gerada e cujo pai está incluído (16 §1).
public sealed record DependencyEdge(
    ObjectName Child, ObjectName Parent, string ForeignKeyName,
    bool IsNullable, bool IsDeferrable, bool IsSelfReference, bool ParentIncluded);

// Grafo de dependências entre tabelas incluídas. Ordem de nós e de arestas é determinística (CanonicalId).
public sealed class DependencyGraph
{
    private readonly List<ObjectName> _nodes;
    private readonly List<DependencyEdge> _edges;

    public DependencyGraph(IEnumerable<ObjectName> nodes, IEnumerable<DependencyEdge> edges)
    {
        _nodes = nodes.Order().ToList();
        _edges = edges.OrderBy(e => e.Child).ThenBy(e => e.Parent).ThenBy(e => e.ForeignKeyName, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<ObjectName> Nodes => _nodes;

    public IReadOnlyList<DependencyEdge> Edges => _edges;

    // Tarjan iterativo (16 §2). Cada componente sai ordenado; a lista sai com pais antes dos filhos
    // (ordem topológica reversa do grafo filho→pai, como o próprio Tarjan produz).
    public IReadOnlyList<IReadOnlyList<ObjectName>> StronglyConnectedComponents()
    {
        var n = _nodes.Count;
        var position = new Dictionary<ObjectName, int>(n);
        for (var i = 0; i < n; i++)
        {
            position[_nodes[i]] = i;
        }

        var adjacency = new List<int>[n];
        for (var i = 0; i < n; i++)
        {
            adjacency[i] = [];
        }

        foreach (var edge in _edges)
        {
            if (position.TryGetValue(edge.Child, out var from) && position.TryGetValue(edge.Parent, out var to))
            {
                adjacency[from].Add(to);
            }
        }

        var index = new int[n];
        var low = new int[n];
        var onStack = new bool[n];
        Array.Fill(index, -1);
        var stack = new Stack<int>();
        var work = new Stack<(int Node, int Next)>();
        var result = new List<IReadOnlyList<ObjectName>>();
        var counter = 0;

        for (var start = 0; start < n; start++)
        {
            if (index[start] != -1)
            {
                continue;
            }

            index[start] = low[start] = counter++;
            stack.Push(start);
            onStack[start] = true;
            work.Push((start, 0));

            while (work.Count > 0)
            {
                var (v, next) = work.Pop();
                if (next < adjacency[v].Count)
                {
                    work.Push((v, next + 1));
                    var w = adjacency[v][next];
                    if (index[w] == -1)
                    {
                        index[w] = low[w] = counter++;
                        stack.Push(w);
                        onStack[w] = true;
                        work.Push((w, 0));
                    }
                    else if (onStack[w])
                    {
                        low[v] = Math.Min(low[v], index[w]);
                    }

                    continue;
                }

                if (low[v] == index[v])
                {
                    var component = new List<ObjectName>();
                    int member;
                    do
                    {
                        member = stack.Pop();
                        onStack[member] = false;
                        component.Add(_nodes[member]);
                    }
                    while (member != v);

                    component.Sort();
                    result.Add(component);
                }

                if (work.Count > 0)
                {
                    var parent = work.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[v]);
                }
            }
        }

        return result;
    }
}

public static class DependencyGraphBuilder
{
    // Monta o grafo com as tabelas incluídas. FKs cuja origem não é Generated não criam aresta (16 §1).
    // FKs para pai excluído são tratadas por FindExcludedParents (diagnóstico PLN-FK-PARENT-EXCLUDED).
    public static DependencyGraph Build(SchemaModel schema, IReadOnlySet<ObjectName> includedTables,
        IReadOnlyDictionary<string, TableSettings> tableSettings)
    {
        var edges = new List<DependencyEdge>();
        foreach (var child in includedTables)
        {
            var table = schema.FindTable(child);
            if (table is null)
            {
                continue;
            }

            foreach (var fk in table.ForeignKeys)
            {
                if (!IsGenerated(child, fk.Name, tableSettings) || !includedTables.Contains(fk.Referenced))
                {
                    continue;
                }

                var isNullable = fk.Columns.All(c => table.FindColumn(c)?.IsNullable == true);
                edges.Add(new DependencyEdge(child, fk.Referenced, fk.Name, isNullable, fk.IsDeferrable,
                    child == fk.Referenced, ParentIncluded: true));
            }
        }

        return new DependencyGraph(includedTables, edges);
    }

    // Pares (filho, FK) cujo pai não está incluído e a origem é Generated: bloqueiam o plano.
    public static IReadOnlyList<(ObjectName Child, string ForeignKeyName, ObjectName Parent)> FindExcludedParents(
        SchemaModel schema, IReadOnlySet<ObjectName> includedTables, IReadOnlyDictionary<string, TableSettings> tableSettings)
    {
        var found = new List<(ObjectName, string, ObjectName)>();
        foreach (var child in includedTables.Order())
        {
            var table = schema.FindTable(child);
            if (table is null)
            {
                continue;
            }

            foreach (var fk in table.ForeignKeys.OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                if (IsGenerated(child, fk.Name, tableSettings) && !includedTables.Contains(fk.Referenced))
                {
                    found.Add((child, fk.Name, fk.Referenced));
                }
            }
        }

        return found;
    }

    public static bool IsGenerated(ObjectName child, string foreignKeyName, IReadOnlyDictionary<string, TableSettings> tableSettings)
    {
        if (!tableSettings.TryGetValue(child.CanonicalId, out var settings) || settings.Relations is null)
        {
            return true;
        }

        return !settings.Relations.TryGetValue(foreignKeyName, out var relation)
            || relation.Source == ReferenceSourceKind.Generated;
    }
}
