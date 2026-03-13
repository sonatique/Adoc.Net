namespace AdocNet.Tools.DifferentialTester;

/// <summary>
/// Computes line-level diffs between two strings for comparison reporting.
/// Uses a simple LCS (Longest Common Subsequence) algorithm.
/// </summary>
public static class DiffEngine
{
    /// <summary>
    /// Computes a diff between two normalized HTML strings.
    /// </summary>
    public static DiffResult Compare(string expected, string actual)
    {
        if (expected == actual)
            return new DiffResult([], 1.0, Identical: true);

        var expectedLines = expected.Split('\n');
        var actualLines = actual.Split('\n');

        var diffLines = ComputeDiff(expectedLines, actualLines);
        double similarity = ComputeSimilarity(expectedLines, actualLines);

        return new DiffResult(diffLines, similarity, Identical: false);
    }

    /// <summary>
    /// Computes the similarity ratio (0.0 to 1.0) between two sets of lines
    /// using LCS length relative to the longer input.
    /// </summary>
    private static double ComputeSimilarity(string[] a, string[] b)
    {
        int maxLen = Math.Max(a.Length, b.Length);
        if (maxLen == 0) return 1.0;

        int lcsLen = LcsLength(a, b);
        return (double)lcsLen / maxLen;
    }

    /// <summary>
    /// Produces a unified-style diff with context lines.
    /// </summary>
    private static List<DiffLine> ComputeDiff(string[] a, string[] b)
    {
        var result = new List<DiffLine>();

        // Simple O(n*m) LCS backtrack approach — fine for our file sizes
        int n = a.Length;
        int m = b.Length;

        // For very large files, fall back to a simpler approach
        if ((long)n * m > 10_000_000)
            return SimpleDiff(a, b);

        var dp = new int[n + 1, m + 1];
        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                if (a[i - 1] == b[j - 1])
                    dp[i, j] = dp[i - 1, j - 1] + 1;
                else
                    dp[i, j] = Math.Max(dp[i - 1, j], dp[i, j - 1]);
            }
        }

        // Backtrack to produce diff
        int ii = n, jj = m;
        var ops = new List<(char op, string line)>();

        while (ii > 0 || jj > 0)
        {
            if (ii > 0 && jj > 0 && a[ii - 1] == b[jj - 1])
            {
                ops.Add((' ', a[ii - 1]));
                ii--;
                jj--;
            }
            else if (jj > 0 && (ii == 0 || dp[ii, jj - 1] >= dp[ii - 1, jj]))
            {
                ops.Add(('+', b[jj - 1]));
                jj--;
            }
            else
            {
                ops.Add(('-', a[ii - 1]));
                ii--;
            }
        }

        ops.Reverse();

        // Convert to DiffLine with context window
        const int contextLines = 3;
        bool lastWasChange = false;
        int lastChangeIdx = -100;

        for (int i = 0; i < ops.Count; i++)
        {
            var (op, line) = ops[i];
            bool isChange = op != ' ';

            if (isChange)
                lastChangeIdx = i;

            // Show context: lines near changes
            bool inContext = isChange
                || (i - lastChangeIdx <= contextLines)
                || (FindNextChange(ops, i) - i <= contextLines);

            if (inContext)
            {
                result.Add(new DiffLine(op switch { '+' => DiffOp.Add, '-' => DiffOp.Remove, _ => DiffOp.Context }, line));
                lastWasChange = true;
            }
            else if (lastWasChange)
            {
                result.Add(new DiffLine(DiffOp.Separator, "..."));
                lastWasChange = false;
            }
        }

        return result;
    }

    private static int FindNextChange(List<(char op, string line)> ops, int fromIndex)
    {
        for (int i = fromIndex + 1; i < ops.Count; i++)
        {
            if (ops[i].op != ' ')
                return i;
        }
        return ops.Count + 100;
    }

    /// <summary>
    /// Fallback for very large files — just shows removed/added without LCS.
    /// </summary>
    private static List<DiffLine> SimpleDiff(string[] a, string[] b)
    {
        var result = new List<DiffLine>();

        var aSet = a.ToHashSet();
        var bSet = b.ToHashSet();

        foreach (var line in a)
        {
            if (!bSet.Contains(line))
                result.Add(new DiffLine(DiffOp.Remove, line));
        }

        foreach (var line in b)
        {
            if (!aSet.Contains(line))
                result.Add(new DiffLine(DiffOp.Add, line));
        }

        return result;
    }

    private static int LcsLength(string[] a, string[] b)
    {
        int n = a.Length;
        int m = b.Length;

        if ((long)n * m > 10_000_000)
        {
            // Approximate for large inputs
            var common = a.Intersect(b).Count();
            return common;
        }

        var prev = new int[m + 1];
        var curr = new int[m + 1];

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                if (a[i - 1] == b[j - 1])
                    curr[j] = prev[j - 1] + 1;
                else
                    curr[j] = Math.Max(prev[j], curr[j - 1]);
            }
            (prev, curr) = (curr, prev);
            Array.Clear(curr, 0, curr.Length);
        }

        return prev[m];
    }
}

/// <summary>
/// Represents a single line in a diff output.
/// </summary>
public sealed record DiffLine(DiffOp Op, string Content);

/// <summary>
/// The operation type for a diff line.
/// </summary>
public enum DiffOp
{
    Context,
    Add,
    Remove,
    Separator,
}

/// <summary>
/// Result of comparing two HTML outputs.
/// </summary>
/// <param name="Lines">The diff lines (empty if identical).</param>
/// <param name="Similarity">Similarity ratio from 0.0 to 1.0.</param>
/// <param name="Identical">Whether the normalized outputs are identical.</param>
public sealed record DiffResult(List<DiffLine> Lines, double Similarity, bool Identical);
