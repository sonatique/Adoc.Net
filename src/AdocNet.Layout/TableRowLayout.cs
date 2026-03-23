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
    /// Creates a new table row layout.
    /// </summary>
    public TableRowLayout(IReadOnlyList<TableCellLayout> cells)
    {
        Cells = cells;
    }
}
