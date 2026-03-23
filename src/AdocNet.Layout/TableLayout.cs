using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A table block containing rows of cells.
/// </summary>
public sealed class TableLayout : BlockLayout
{
    /// <summary>
    /// Optional table title/caption.
    /// </summary>
    public string? Title { get; }

    /// <summary>
    /// Whether the first row is a header row.
    /// </summary>
    public bool HasHeader { get; }

    /// <summary>
    /// Whether the last row is a footer row.
    /// </summary>
    public bool HasFooter { get; }

    /// <summary>
    /// The table rows.
    /// </summary>
    public IReadOnlyList<TableRowLayout> Rows { get; }

    /// <summary>
    /// Creates a new table layout.
    /// </summary>
    public TableLayout(string? title, bool hasHeader, bool hasFooter, IReadOnlyList<TableRowLayout> rows)
    {
        Title = title;
        HasHeader = hasHeader;
        HasFooter = hasFooter;
        Rows = rows;
    }
}
