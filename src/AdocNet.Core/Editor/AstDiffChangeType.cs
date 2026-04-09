namespace AdocNet.Editor;

/// <summary>
/// Describes the type of change detected for a top-level document block
/// when comparing two AST trees.
/// </summary>
public enum AstDiffChangeType
{
    /// <summary>Block unchanged (structural hash match).</summary>
    Unchanged,

    /// <summary>Block content modified (structural hash mismatch).</summary>
    Modified,

    /// <summary>New block added (no corresponding old block).</summary>
    Added,

    /// <summary>Old block removed (no corresponding new block).</summary>
    Removed,
}
