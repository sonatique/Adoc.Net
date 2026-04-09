using AdocNet.Ast;

namespace AdocNet.Editor;

/// <summary>
/// Compares two document ASTs and identifies which top-level blocks changed.
/// Uses structural hashing for O(1) per-block comparison and a two-pass
/// matching strategy: ID-based for named sections, positional for the rest.
/// </summary>
public static class AstDiffer
{
    /// <summary>
    /// Compares the top-level children of two documents and returns a list of
    /// diff entries describing what changed.
    /// </summary>
    /// <param name="oldDoc">The previous document AST.</param>
    /// <param name="newDoc">The new document AST.</param>
    /// <returns>Diff entries ordered by their index in the result document.</returns>
    public static IReadOnlyList<AstDiffEntry> DiffSections(DocumentNode oldDoc, DocumentNode newDoc)
    {
        if (oldDoc is null) throw new ArgumentNullException(nameof(oldDoc));
        if (newDoc is null) throw new ArgumentNullException(nameof(newDoc));

        var oldChildren = oldDoc.Children;
        var newChildren = newDoc.Children;

        if (oldChildren.Count == 0 && newChildren.Count == 0)
            return [];

        if (oldChildren.Count == 0)
            return AllAdded(newChildren);

        if (newChildren.Count == 0)
            return AllRemoved(oldChildren);

        return MatchSections(oldChildren, newChildren);
    }

    private static List<AstDiffEntry> MatchSections(
        IReadOnlyList<AstNode> oldChildren,
        IReadOnlyList<AstNode> newChildren)
    {
        // Track which old/new indices have been matched
        var oldMatched = new bool[oldChildren.Count];
        var newMatched = new bool[newChildren.Count];
        // Map new index -> old index for matched pairs
        var matchPairs = new Dictionary<int, int>();

        // Pass 1: Match by section ID (stable across reordering)
        MatchById(oldChildren, newChildren, oldMatched, newMatched, matchPairs);

        // Pass 2: Match remaining by position
        MatchByPosition(oldChildren, newChildren, oldMatched, newMatched, matchPairs);

        // Build result
        return BuildResult(oldChildren, newChildren, oldMatched, newMatched, matchPairs);
    }

    private static void MatchById(
        IReadOnlyList<AstNode> oldChildren,
        IReadOnlyList<AstNode> newChildren,
        bool[] oldMatched,
        bool[] newMatched,
        Dictionary<int, int> matchPairs)
    {
        // Build index of old sections by ID
        var oldIdToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < oldChildren.Count; i++)
        {
            if (oldChildren[i] is BlockNode { Id: not null } block)
                oldIdToIndex.TryAdd(block.Id, i);
        }

        if (oldIdToIndex.Count == 0) return;

        // Match new sections to old by ID
        for (int j = 0; j < newChildren.Count; j++)
        {
            if (newChildren[j] is BlockNode { Id: not null } block
                && oldIdToIndex.TryGetValue(block.Id, out int oldIdx)
                && !oldMatched[oldIdx])
            {
                oldMatched[oldIdx] = true;
                newMatched[j] = true;
                matchPairs[j] = oldIdx;
            }
        }
    }

    private static void MatchByPosition(
        IReadOnlyList<AstNode> oldChildren,
        IReadOnlyList<AstNode> newChildren,
        bool[] oldMatched,
        bool[] newMatched,
        Dictionary<int, int> matchPairs)
    {
        int oldIdx = 0;
        int newIdx = 0;

        while (oldIdx < oldChildren.Count && newIdx < newChildren.Count)
        {
            // Advance past already-matched indices
            while (oldIdx < oldChildren.Count && oldMatched[oldIdx]) oldIdx++;
            while (newIdx < newChildren.Count && newMatched[newIdx]) newIdx++;

            if (oldIdx < oldChildren.Count && newIdx < newChildren.Count)
            {
                oldMatched[oldIdx] = true;
                newMatched[newIdx] = true;
                matchPairs[newIdx] = oldIdx;
                oldIdx++;
                newIdx++;
            }
        }
    }

    private static List<AstDiffEntry> BuildResult(
        IReadOnlyList<AstNode> oldChildren,
        IReadOnlyList<AstNode> newChildren,
        bool[] oldMatched,
        bool[] newMatched,
        Dictionary<int, int> matchPairs)
    {
        var result = new List<AstDiffEntry>();

        // Emit Removed entries for unmatched old children (in old-index order)
        for (int i = 0; i < oldChildren.Count; i++)
        {
            if (!oldMatched[i])
            {
                result.Add(new AstDiffEntry
                {
                    Index = i,
                    ChangeType = AstDiffChangeType.Removed,
                    OldNode = oldChildren[i],
                    NewNode = null,
                });
            }
        }

        // Emit entries for new children in order
        for (int j = 0; j < newChildren.Count; j++)
        {
            if (!newMatched[j])
            {
                result.Add(new AstDiffEntry
                {
                    Index = j,
                    ChangeType = AstDiffChangeType.Added,
                    OldNode = null,
                    NewNode = newChildren[j],
                });
            }
            else if (matchPairs.TryGetValue(j, out int oldIdx))
            {
                var oldNode = oldChildren[oldIdx];
                var newNode = newChildren[j];
                var changeType = oldNode.StructuralHash == newNode.StructuralHash
                    ? AstDiffChangeType.Unchanged
                    : AstDiffChangeType.Modified;

                result.Add(new AstDiffEntry
                {
                    Index = j,
                    ChangeType = changeType,
                    OldNode = oldNode,
                    NewNode = newNode,
                });
            }
        }

        return result;
    }

    private static List<AstDiffEntry> AllAdded(IReadOnlyList<AstNode> children)
    {
        var result = new List<AstDiffEntry>(children.Count);
        for (int i = 0; i < children.Count; i++)
        {
            result.Add(new AstDiffEntry
            {
                Index = i,
                ChangeType = AstDiffChangeType.Added,
                OldNode = null,
                NewNode = children[i],
            });
        }
        return result;
    }

    private static List<AstDiffEntry> AllRemoved(IReadOnlyList<AstNode> children)
    {
        var result = new List<AstDiffEntry>(children.Count);
        for (int i = 0; i < children.Count; i++)
        {
            result.Add(new AstDiffEntry
            {
                Index = i,
                ChangeType = AstDiffChangeType.Removed,
                OldNode = children[i],
                NewNode = null,
            });
        }
        return result;
    }
}
