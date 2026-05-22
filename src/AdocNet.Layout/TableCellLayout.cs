using System.Collections.Generic;

namespace AdocNet.Layout;

/// <summary>
/// A single cell in a table row, containing inline content and optional span.
/// </summary>
public sealed class TableCellLayout
{
    /// <summary>
    /// The inline content of the cell.
    /// </summary>
    public IReadOnlyList<InlineLayout> Inlines { get; }

    /// <summary>
    /// Number of columns this cell spans.
    /// </summary>
    public int ColSpan { get; }

    /// <summary>
    /// Number of rows this cell spans.
    /// </summary>
    public int RowSpan { get; }

    /// <summary>
    /// Whether this cell is a header cell.
    /// </summary>
    public bool IsHeader { get; }

    /// <summary>
    /// Source range of the originating <c>TableCellNode</c>. Populated by
    /// <see cref="Builders.LayoutBuilder"/>; defaults to
    /// <see cref="SourceRange.None"/> when the cell was constructed
    /// directly. Enables hover-to-source and per-cell diagnostics.
    /// </summary>
    public SourceRange Source { get; init; } = SourceRange.None;

    /// <summary>
    /// Creates a new table cell layout.
    /// </summary>
    public TableCellLayout(IReadOnlyList<InlineLayout> inlines, int colSpan, int rowSpan, bool isHeader)
    {
        Inlines = inlines;
        ColSpan = colSpan;
        RowSpan = rowSpan;
        IsHeader = isHeader;
    }
}
