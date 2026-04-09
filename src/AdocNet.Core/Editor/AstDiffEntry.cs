using AdocNet.Ast;

namespace AdocNet.Editor;

/// <summary>
/// Describes a change to a top-level block when comparing two document ASTs.
/// </summary>
public readonly struct AstDiffEntry
{
    /// <summary>
    /// Index in the relevant document's children list.
    /// For <see cref="AstDiffChangeType.Removed"/> entries, this is the index in the old document.
    /// For all other entries, this is the index in the new document.
    /// </summary>
    public int Index { get; init; }

    /// <summary>The type of change.</summary>
    public AstDiffChangeType ChangeType { get; init; }

    /// <summary>
    /// The node from the old document, or null for <see cref="AstDiffChangeType.Added"/> entries.
    /// </summary>
    public AstNode? OldNode { get; init; }

    /// <summary>
    /// The node from the new document, or null for <see cref="AstDiffChangeType.Removed"/> entries.
    /// </summary>
    public AstNode? NewNode { get; init; }
}
