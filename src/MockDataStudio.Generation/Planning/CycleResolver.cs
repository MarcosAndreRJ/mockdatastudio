using MockDataStudio.Core.Naming;
using MockDataStudio.Core.Profiles;

namespace MockDataStudio.Generation.Planning;

public sealed record DeferredEdge(DependencyEdge Edge, CycleBreakStrategy Strategy);

public sealed record CycleResolution(bool Breakable, IReadOnlyList<DeferredEdge> Deferred);

// Resolução de ciclos (16 §3): escolhe o menor conjunto de arestas a adiar que deixa o componente acíclico.
// Autorreferência não entra aqui: é tratada pelo gerador (16 §4).
public static class CycleResolver
{
    // Até este tamanho a busca é exaustiva (determinística); acima, guloso (heurístico, registrado como limitação).
    public const int ExhaustiveEdgeLimit = 12;

    public static CycleResolution Resolve(IReadOnlyList<ObjectName> component, IReadOnlyList<DependencyEdge> edges,
        bool allowDeferred, bool allowDisableChecks)
    {
        var internalEdges = edges
            .Where(e => !e.IsSelfReference && component.Contains(e.Child) && component.Contains(e.Parent))
            .OrderBy(e => e.Child).ThenBy(e => e.Parent).ThenBy(e => e.ForeignKeyName, StringComparer.Ordinal)
            .ToList();

        if (IsAcyclic(component, internalEdges, new HashSet<DependencyEdge>()))
        {
            return new CycleResolution(true, []);
        }

        // Candidatas: a estratégia de menor custo aplicável à aresta. Ordem de preferência: NullThenUpdate, Deferred, DisableChecks.
        var candidates = new List<(DependencyEdge Edge, CycleBreakStrategy Strategy)>();
        foreach (var edge in internalEdges)
        {
            if (edge.IsNullable)
            {
                candidates.Add((edge, CycleBreakStrategy.NullThenUpdate));
            }
            else if (allowDeferred && edge.IsDeferrable)
            {
                candidates.Add((edge, CycleBreakStrategy.DeferredConstraint));
            }
            else if (allowDisableChecks)
            {
                candidates.Add((edge, CycleBreakStrategy.DisableChecks));
            }
        }

        if (candidates.Count == 0)
        {
            return new CycleResolution(false, []);
        }

        return candidates.Count <= ExhaustiveEdgeLimit
            ? Exhaustive(component, internalEdges, candidates)
            : Greedy(component, internalEdges, candidates);
    }

    private static CycleResolution Exhaustive(IReadOnlyList<ObjectName> component, IReadOnlyList<DependencyEdge> edges,
        List<(DependencyEdge Edge, CycleBreakStrategy Strategy)> candidates)
    {
        var count = candidates.Count;
        for (var size = 1; size <= count; size++)
        {
            (List<(DependencyEdge, CycleBreakStrategy)> Set, int Cost)? best = null;
            foreach (var subset in Combinations(count, size))
            {
                var removed = subset.Select(i => candidates[i].Edge).ToHashSet();
                if (!IsAcyclic(component, edges, removed))
                {
                    continue;
                }

                var set = subset.Select(i => candidates[i]).ToList();
                var cost = set.Sum(s => (int)s.Strategy);
                if (best is null || cost < best.Value.Cost)
                {
                    best = (set, cost);
                }
            }

            if (best is { } found)
            {
                return new CycleResolution(true, found.Set.Select(s => new DeferredEdge(s.Item1, s.Item2)).ToList());
            }
        }

        return new CycleResolution(false, []);
    }

    private static CycleResolution Greedy(IReadOnlyList<ObjectName> component, IReadOnlyList<DependencyEdge> edges,
        List<(DependencyEdge Edge, CycleBreakStrategy Strategy)> candidates)
    {
        var deferred = new List<DeferredEdge>();
        var removed = new HashSet<DependencyEdge>();
        while (!IsAcyclic(component, edges, removed))
        {
            // Só remove aresta que está dentro de um ciclo do grafo restante (não desperdiça adiamentos).
            var remaining = edges.Where(e => !removed.Contains(e)).ToList();
            var sccOf = new Dictionary<ObjectName, int>();
            var sccs = new DependencyGraph(component, remaining).StronglyConnectedComponents();
            for (var i = 0; i < sccs.Count; i++)
            {
                foreach (var node in sccs[i])
                {
                    sccOf[node] = i;
                }
            }

            var pick = candidates.FirstOrDefault(c =>
                !removed.Contains(c.Edge)
                && sccOf[c.Edge.Child] == sccOf[c.Edge.Parent]
                && sccs[sccOf[c.Edge.Child]].Count > 1);
            if (pick.Edge is null)
            {
                return new CycleResolution(false, []);
            }

            removed.Add(pick.Edge);
            deferred.Add(new DeferredEdge(pick.Edge, pick.Strategy));
        }

        return new CycleResolution(true, deferred);
    }

    // Kahn: acíclico quando todos os nós são consumidos ao remover as arestas listadas em removed.
    private static bool IsAcyclic(IReadOnlyList<ObjectName> component, IReadOnlyList<DependencyEdge> edges, HashSet<DependencyEdge> removed)
    {
        var indegree = component.ToDictionary(n => n, _ => 0);
        var active = edges.Where(e => !removed.Contains(e)).ToList();
        foreach (var edge in active)
        {
            indegree[edge.Child]++;
        }

        var queue = new Queue<ObjectName>(indegree.Where(kv => kv.Value == 0).Select(kv => kv.Key).Order());
        var seen = 0;
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            seen++;
            foreach (var edge in active.Where(e => e.Parent == node).OrderBy(e => e.Child))
            {
                indegree[edge.Child]--;
                if (indegree[edge.Child] == 0)
                {
                    queue.Enqueue(edge.Child);
                }
            }
        }

        return seen == component.Count;
    }

    // Combinações de 'size' índices em ordem lexicográfica.
    private static IEnumerable<int[]> Combinations(int n, int size)
    {
        var current = Enumerable.Range(0, size).ToArray();
        while (true)
        {
            yield return (int[])current.Clone();
            var i = size - 1;
            while (i >= 0 && current[i] == n - size + i)
            {
                i--;
            }

            if (i < 0)
            {
                yield break;
            }

            current[i]++;
            for (var j = i + 1; j < size; j++)
            {
                current[j] = current[j - 1] + 1;
            }
        }
    }
}
