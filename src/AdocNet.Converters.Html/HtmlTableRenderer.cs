using System.Text;
using System.Text.RegularExpressions;
using AdocNet;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Converters.Html;

public sealed partial class HtmlRenderer
{
    private void RenderTable(StringBuilder sb, TableNode table, bool useIconFont, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        sb.Append("<table");
        if (table.Id is not null)
        {
            sb.Append(" id=\"");
            EscapeTo(sb, table.Id);
            sb.Append('"');
        }

        // Build CSS class list from table options (Asciidoctor always emits frame/grid/tableblock)
        var tableClasses = new List<string>();
        tableClasses.Add($"frame-{table.Frame ?? "all"}");
        tableClasses.Add($"grid-{table.Grid ?? "all"}");
        if (table.IsAutoWidth)
            tableClasses.Add("fit-content");
        else
            tableClasses.Add("stretch");
        if (table.Stripes is not null)
            tableClasses.Add($"stripes-{table.Stripes}");
        tableClasses.Add("tableblock");
        AppendRoleClasses(sb, table, string.Join(" ", tableClasses));

        sb.Append(">\n");

        if (table.Title is not null)
        {
            var tableCaption = state.DocumentAttributes.TryGetValue("table-caption", out var customTableCaption) ? customTableCaption : "Table";
            sb.Append("<caption class=\"title\">");
            EscapeTo(sb, tableCaption);
            sb.Append(' ');
            sb.Append(state.TableCounter);
            sb.Append(". ");
            EscapeTo(sb, table.Title);
            sb.Append("</caption>\n");
            state.TableCounter++;
        }

        // Render colgroup — Asciidoctor always emits it.
        // For autowidth tables, emit unstyled <col> elements.
        // For fixed-width tables, use proportional widths.
        if (table.IsAutoWidth)
        {
            int colCount = 0;
            if (table.Children.Count > 0 && table.Children[0] is TableRowNode firstAutoRow)
                colCount = firstAutoRow.Children.Count;
            if (colCount > 0)
            {
                sb.Append("<colgroup>\n");
                for (int ci = 0; ci < colCount; ci++)
                    sb.Append("<col>\n");
                sb.Append("</colgroup>\n");
            }
        }
        else
        {
            if (table.Columns is { Count: > 0 })
            {
                int totalWidth = 0;
                foreach (var col in table.Columns)
                    totalWidth += col.Width;

                sb.Append("<colgroup>\n");
                double emittedTruncated = 0;
                for (int ci = 0; ci < table.Columns.Count; ci++)
                {
                    double rawPct = 100.0 * table.Columns[ci].Width / totalWidth;
                    // Asciidoctor truncates each column to 4 decimal places (not rounding),
                    // accumulates truncated values, and gives the remainder to the last column.
                    double colPct;
                    if (ci == table.Columns.Count - 1)
                        colPct = TruncateTo4(100.0 - emittedTruncated);
                    else
                        colPct = TruncateTo4(rawPct);
                    sb.Append("<col style=\"width: ");
                    if (Math.Abs(colPct - Math.Truncate(colPct)) < 0.00005)
                        sb.Append((int)colPct);
                    else
                        sb.AppendFormat("{0:F4}", colPct);
                    sb.Append("%;\">\n");
                    emittedTruncated += colPct;
                }
                sb.Append("</colgroup>\n");
            }
            else
            {
                // No explicit cols — derive column count from the first row (sum colspans)
                int colCount = 0;
                if (table.Children.Count > 0 && table.Children[0] is TableRowNode firstRow)
                {
                    foreach (var child in firstRow.Children)
                        colCount += child is TableCellNode cell ? cell.ColSpan : 1;
                }

                if (colCount > 0)
                {
                    double rawPct = 100.0 / colCount;
                    sb.Append("<colgroup>\n");
                    double emittedTruncated = 0;
                    for (int ci = 0; ci < colCount; ci++)
                    {
                        double colPct;
                        if (ci == colCount - 1)
                            colPct = TruncateTo4(100.0 - emittedTruncated);
                        else
                            colPct = TruncateTo4(rawPct);
                        sb.Append("<col style=\"width: ");
                        if (Math.Abs(colPct - Math.Truncate(colPct)) < 0.00005)
                            sb.Append((int)colPct);
                        else
                            sb.AppendFormat("{0:F4}", colPct);
                        sb.Append("%;\">\n");
                        emittedTruncated += colPct;
                    }
                    sb.Append("</colgroup>\n");
                }
            }
        }

        int startRow = 0;
        if (table.HasHeader && table.Children.Count > 0)
        {
            sb.Append("<thead>\n");
            if (table.Children[0] is TableRowNode headerRow)
                RenderTableRow(sb, headerRow, "th", table.Columns, useIconFont, footnotes, secCtx, state);
            sb.Append("</thead>\n");
            startRow = 1;
        }

        int endRow = table.Children.Count;
        if (table.HasFooter && table.Children.Count > startRow)
            endRow = table.Children.Count - 1;

        sb.Append("<tbody>\n");
        for (int i = startRow; i < endRow; i++)
        {
            if (table.Children[i] is TableRowNode row)
                RenderTableRow(sb, row, "td", table.Columns, useIconFont, footnotes, secCtx, state);
        }
        sb.Append("</tbody>\n");

        if (table.HasFooter && table.Children.Count > startRow)
        {
            sb.Append("<tfoot>\n");
            if (table.Children[^1] is TableRowNode footerRow)
                RenderTableRow(sb, footerRow, "td", table.Columns, useIconFont, footnotes, secCtx, state);
            sb.Append("</tfoot>\n");
        }

        sb.Append("</table>\n");
    }

    private void RenderTableRow(StringBuilder sb, TableRowNode row, string cellTag,
        IReadOnlyList<TableColumnSpec>? columns, bool useIconFont, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        sb.Append("<tr>\n");
        int colIndex = 0;
        foreach (var child in row.Children)
        {
            if (child is TableCellNode cell)
            {
                // Header style overrides cell tag to <th>
                var effectiveTag = cell.ContentStyle == TableCellStyle.Header ? "th" : cellTag;

                sb.Append('<');
                sb.Append(effectiveTag);

                if (cell.ColSpan > 1)
                {
                    sb.Append(" colspan=\"");
                    sb.Append(cell.ColSpan);
                    sb.Append('"');
                }

                if (cell.RowSpan > 1)
                {
                    sb.Append(" rowspan=\"");
                    sb.Append(cell.RowSpan);
                    sb.Append('"');
                }

                // Determine alignment: per-cell override, then column spec, then default (left)
                var hAlign = cell.Alignment;
                if (hAlign is null && columns is not null && colIndex < columns.Count)
                    hAlign = columns[colIndex].Alignment;
                hAlign ??= TableAlignment.Left;

                var vAlign = cell.VerticalAlignment;
                if (vAlign is null && columns is not null && colIndex < columns.Count)
                    vAlign = columns[colIndex].VerticalAlignment;
                vAlign ??= TableVerticalAlignment.Top;

                // Asciidoctor emits halign-*/valign-*/tableblock classes on every cell
                var hAlignClass = hAlign switch
                {
                    TableAlignment.Center => "halign-center",
                    TableAlignment.Right => "halign-right",
                    _ => "halign-left",
                };
                var vAlignClass = vAlign switch
                {
                    TableVerticalAlignment.Middle => "valign-middle",
                    TableVerticalAlignment.Bottom => "valign-bottom",
                    _ => "valign-top",
                };
                sb.Append($" class=\"{hAlignClass} tableblock {vAlignClass}\"");

                sb.Append('>');

                // Wrap content based on cell style
                var wrapOpen = cell.ContentStyle switch
                {
                    TableCellStyle.Emphasis  => "<em>",
                    TableCellStyle.Literal   => "<pre>",
                    TableCellStyle.Monospace  => "<code>",
                    _ => null,
                };
                var wrapClose = cell.ContentStyle switch
                {
                    TableCellStyle.Emphasis  => "</em>",
                    TableCellStyle.Literal   => "</pre>",
                    TableCellStyle.Monospace  => "</code>",
                    _ => null,
                };

                if (cell.ContentStyle == TableCellStyle.AsciiDoc && cell.Children.Count > 0)
                {
                    foreach (var blockChild in cell.Children)
                        RenderBlock(sb, blockChild, useIconFont, footnotes, secCtx, state);
                }
                else
                {
                    // Asciidoctor wraps body cell content in <p class="tableblock">
                    // but skips the wrapper for actual header row cells and empty cells.
                    // h-style cells (ContentStyle == Header) render as <th> but ARE
                    // body cells, so they still get the <p> wrapper.
                    bool hasContent = cell.Inlines.Count > 0 || !string.IsNullOrEmpty(cell.Text);
                    bool wrapInP = cellTag == "td" && hasContent;

                    if (wrapInP)
                        sb.Append("<p class=\"tableblock\">");
                    if (wrapOpen is not null)
                        sb.Append(wrapOpen);
                    RenderInlines(sb, cell.Inlines, cell.Text, footnotes, state);
                    if (wrapClose is not null)
                        sb.Append(wrapClose);
                    if (wrapInP)
                        sb.Append("</p>");
                }

                sb.Append("</");
                sb.Append(effectiveTag);
                sb.Append(">\n");

                colIndex += cell.ColSpan;
            }
        }
        sb.Append("</tr>\n");
    }
}
