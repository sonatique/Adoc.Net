namespace AdocNet.Extensions;

/// <summary>
/// Resolves extension load order by topologically sorting the dependency graph.
/// Uses Kahn's algorithm (BFS-based topological sort) to produce a deterministic
/// ordering where dependencies are loaded before the extensions that depend on them.
/// </summary>
public static class DependencyResolver
{
    /// <summary>
    /// Returns extension names in dependency order (dependencies load first).
    /// Extensions with no dependencies appear first, in the order they were provided.
    /// Dependencies that reference names not in the input list are ignored
    /// (they are external/missing and handled separately by <see cref="DependencyValidator"/>).
    /// </summary>
    /// <param name="extensions">List of (name, dependencies) pairs to sort.</param>
    /// <returns>Names in topological order — safe to load sequentially.</returns>
    /// <exception cref="InvalidOperationException">Thrown when a dependency cycle is detected.</exception>
    public static IReadOnlyList<string> Resolve(
        IReadOnlyList<(string Name, IReadOnlyList<string> Dependencies)> extensions)
    {
        if (extensions is null)
            throw new ArgumentNullException(nameof(extensions));

        if (extensions.Count == 0)
            return Array.Empty<string>();

        // Build the set of known extension names
        var knownNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ext in extensions)
            knownNames.Add(ext.Name);

        // Build adjacency list and in-degree map
        // Edge: dependency -> dependent (dependency must load first)
        var adjacency = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var ext in extensions)
        {
            if (!adjacency.ContainsKey(ext.Name))
                adjacency[ext.Name] = new List<string>();
            if (!inDegree.ContainsKey(ext.Name))
                inDegree[ext.Name] = 0;

            foreach (var dep in ext.Dependencies)
            {
                var depName = ExtractDependencyName(dep);
                if (depName is null || !knownNames.Contains(depName))
                    continue; // Unknown dependency — skip (handled by DependencyValidator)

                if (!adjacency.ContainsKey(depName))
                    adjacency[depName] = new List<string>();

                adjacency[depName].Add(ext.Name);
                inDegree[ext.Name] = inDegree.TryGetValue(ext.Name, out var d) ? d + 1 : 1;
            }
        }

        // Kahn's algorithm: BFS from nodes with in-degree 0
        var queue = new Queue<string>();
        // Seed with zero in-degree nodes in original input order (stable sort)
        foreach (var ext in extensions)
        {
            if (inDegree.TryGetValue(ext.Name, out var deg) && deg == 0)
                queue.Enqueue(ext.Name);
        }

        var result = new List<string>(extensions.Count);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            result.Add(current);

            if (!adjacency.TryGetValue(current, out var dependents))
                continue;

            foreach (var dependent in dependents)
            {
                inDegree[dependent]--;
                if (inDegree[dependent] == 0)
                    queue.Enqueue(dependent);
            }
        }

        if (result.Count < knownNames.Count)
        {
            // Cycle detected — find the nodes involved
            var cycleNodes = new List<string>();
            foreach (var ext in extensions)
            {
                if (!result.Contains(ext.Name))
                    cycleNodes.Add(ext.Name);
            }

            throw new InvalidOperationException(
                $"Dependency cycle detected among extensions: {string.Join(" -> ", cycleNodes)}");
        }

        return result;
    }

    /// <summary>
    /// Extracts the extension name from a dependency string like "name >= version" or "name".
    /// </summary>
    private static string? ExtractDependencyName(string dependency)
    {
        if (string.IsNullOrWhiteSpace(dependency))
            return null;

        var trimmed = dependency.Trim();
        var geIdx = trimmed.IndexOf(">=", StringComparison.Ordinal);

        if (geIdx >= 0)
        {
            var name = trimmed.Substring(0, geIdx).Trim();
            return name.Length > 0 ? name : null;
        }

        return trimmed;
    }
}
