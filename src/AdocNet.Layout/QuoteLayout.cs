using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A quote or verse block: its content blocks and the optional attribution and citation.
/// </summary>
public sealed class QuoteLayout : BlockLayout
{
    /// <summary>The quoted blocks.</summary>
    public IReadOnlyList<BlockLayout> Blocks { get; }

    /// <summary>Who is quoted, or null.</summary>
    public string? Attribution { get; init; }

    /// <summary>Where the quote comes from, or null.</summary>
    public string? Citation { get; init; }

    public QuoteLayout(IReadOnlyList<BlockLayout> blocks)
    {
        Blocks = blocks;
    }
}
