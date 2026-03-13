using System.Linq;

namespace AdocNet.Ast;

/// <summary>
/// A table block node. Children are <see cref="TableRowNode"/> instances.
/// When <see cref="HasHeader"/> is true, the first child row is a header row.
/// </summary>
public sealed class TableNode : BlockNode
{
    public override AstNodeKind Kind => AstNodeKind.Table;

    /// <summary>
    /// Optional title for the table, rendered as a <c>&lt;caption&gt;</c> element.
    /// Set from a <c>.Title</c> line preceding the table.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// When true, the first row should be rendered as a header row (thead/th).
    /// Set when <c>[options="header"]</c> precedes the table or when the first row
    /// is followed by a blank line.
    /// </summary>
    public bool HasHeader { get; init; }

    /// <summary>
    /// When true, the table should use automatic column widths instead of fixed percentages.
    /// Set when <c>[%autowidth]</c> precedes the table.
    /// </summary>
    public bool IsAutoWidth { get; init; }

    /// <summary>
    /// When true, the last row should be rendered as a footer row (tfoot).
    /// Set when <c>[%footer]</c> or <c>[options="footer"]</c> precedes the table.
    /// </summary>
    public bool HasFooter { get; init; }

    /// <summary>
    /// Parsed column specifications from <c>[cols="..."]</c>, or null if not specified.
    /// </summary>
    public IReadOnlyList<TableColumnSpec>? Columns { get; init; }

    /// <summary>
    /// Table striping mode: "even", "odd", "all", or "none". Null when not specified.
    /// Rendered as <c>class="stripes-{value}"</c> on the table element.
    /// </summary>
    public string? Stripes { get; init; }

    /// <summary>
    /// Table grid mode: "all" (default), "cols", "rows", or "none". Null when not specified.
    /// Rendered as <c>class="grid-{value}"</c> on the table element (except "all", which is the default).
    /// </summary>
    public string? Grid { get; init; }

    /// <summary>
    /// Table frame mode: "all" (default), "topbot", "sides", or "none". Null when not specified.
    /// Rendered as <c>class="frame-{value}"</c> on the table element (except "all", which is the default).
    /// </summary>
    public string? Frame { get; init; }

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        if (HasHeader)
            yield return new("HasHeader", "True");
        if (IsAutoWidth)
            yield return new("IsAutoWidth", "True");
        if (HasFooter)
            yield return new("HasFooter", "True");
        if (Stripes is not null)
            yield return new("Stripes", Stripes);
        if (Grid is not null)
            yield return new("Grid", Grid);
        if (Frame is not null)
            yield return new("Frame", Frame);
        if (Columns is not null)
        {
            var colStrs = Columns.Select(c =>
            {
                var alignPrefix = c.Alignment switch
                {
                    TableAlignment.Center => "^",
                    TableAlignment.Right  => ">",
                    _                     => "",
                };
                return $"{alignPrefix}{c.Width}";
            });
            yield return new("Columns", string.Join(",", colStrs));
        }
    }
}
