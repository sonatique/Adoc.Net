namespace AdocNet.Ast;

/// <summary>
/// A back-of-book index block, triggered by <c>index::[]</c>.
/// The actual terms are collected from <see cref="IndexTermNode"/> and
/// <see cref="IndexTermHiddenNode"/> nodes across the document.
/// </summary>
public sealed class IndexNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.Index;

    /// <summary>
    /// Sorted, deduplicated index entries grouped by first letter.
    /// Each entry contains the primary term and optional sub-terms.
    /// </summary>
    public IReadOnlyList<IndexEntry> Entries { get; set; } = [];

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("EntryCount", Entries.Count.ToString());
    }
}

/// <summary>
/// A single entry in the back-of-book index.
/// </summary>
public sealed class IndexEntry
{
    /// <summary>The primary term.</summary>
    public required string Term { get; init; }

    /// <summary>Optional secondary sub-terms.</summary>
    public IReadOnlyList<string> SubTerms { get; init; } = [];
}
