using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A single row in a table.
/// </summary>
public sealed class TableRowLayout
{
    /// <summary>
    /// The cells in this row.
    /// </summary>
    public IReadOnlyList<TableCellLayout> Cells { get; }

    /// <summary>
    /// Source range of the originating <c>TableRowNode</c> (the source line
    /// where the row begins through the line where its content ends).
    /// Populated by <see cref="Builders.LayoutBuilder"/>; defaults to
    /// <see cref="SourceRange.None"/> when the row was constructed
    /// directly. Used by editor sync-scroll and per-row navigation.
    /// </summary>
    public SourceRange Source { get; init; } = SourceRange.None;

    /// <summary>
    /// Creates a new table row layout.
    /// </summary>
    public TableRowLayout(IReadOnlyList<TableCellLayout> cells)
    {
        Cells = cells;
    }
}
