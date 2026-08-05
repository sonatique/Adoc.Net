using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AdocNet.Ast;
using AdocNet.Emitter;

namespace AdocNet.Importers.Docx;

/// <summary>
/// Converts <c>w:tbl</c> to a <see cref="TableNode"/>: grid widths, header
/// rows, horizontal (<c>w:gridSpan</c>) and vertical (<c>w:vMerge</c>) spans,
/// and per-cell block content.
/// </summary>
internal sealed class TableConverter
{
    private static readonly Regex AdmonitionPrefix = new(
        @"^(NOTE|TIP|IMPORTANT|WARNING|CAUTION)\s*:\s*", RegexOptions.CultureInvariant);

    private readonly ConversionContext _ctx;

    public TableConverter(ConversionContext ctx) => _ctx = ctx;

    public TableNode? Convert(XElement tbl)
    {
        var rows = new List<XElement>(tbl.Elements(Ns.W + "tr"));
        if (rows.Count == 0)
        {
            _ctx.Report.Lost("table.empty", "Table without rows dropped.");
            return null;
        }

        _ctx.Report.Tables++;
        _ctx.Report.Count(mapped: true);
        ReportUnsupportedTableProperties(tbl);

        var table = new TableNode
        {
            HasHeader = HasHeaderRow(rows),
            Columns = ReadColumns(tbl),
        };

        for (var r = 0; r < rows.Count; r++)
        {
            var rowNode = new TableRowNode();
            var gridColumn = 0;

            foreach (var tc in rows[r].Elements(Ns.W + "tc"))
            {
                var tcPr = tc.Element(Ns.W + "tcPr");
                var colSpan = ReadInt(tcPr?.Element(Ns.W + "gridSpan").WVal()) ?? 1;
                var vMerge = tcPr?.Element(Ns.W + "vMerge");

                if (vMerge is not null && (vMerge.WVal() ?? "continue") != "restart")
                {
                    // Continuation of a vertical merge: AsciiDoc expresses the
                    // span on the originating cell, so this one is skipped.
                    gridColumn += colSpan;
                    continue;
                }

                var rowSpan = vMerge is null ? 1 : MeasureRowSpan(rows, r, gridColumn);
                rowNode.AddChild(ConvertCell(tc, colSpan, rowSpan));
                gridColumn += colSpan;
            }

            if (rowNode.Children.Count > 0) table.AddChild(rowNode);
        }

        return table;
    }

    /// <summary>
    /// Recognises the single-cell table Word users build for callout boxes and
    /// converts it to an admonition when its text opens with a known label.
    /// </summary>
    public bool TryConvertAdmonition(XElement tbl, out AdmonitionNode? admonition)
    {
        admonition = null;

        var rows = new List<XElement>(tbl.Elements(Ns.W + "tr"));
        if (rows.Count != 1) return false;

        var cells = new List<XElement>(rows[0].Elements(Ns.W + "tc"));
        if (cells.Count != 1) return false;

        var blocks = ConvertCellBlocks(cells[0]);
        if (blocks.Count == 0) return false;

        // The cell's own paragraph conversion may already have recognised the
        // label, in which case the box just unwraps to that admonition.
        if (blocks[0] is AdmonitionNode inner)
        {
            _ctx.Report.Tables++;
            _ctx.Report.Count(mapped: true);

            if (blocks.Count == 1)
            {
                admonition = inner;
                return true;
            }

            var combined = new AdmonitionNode { AdmonitionType = inner.AdmonitionType, Title = inner.Title };
            if (inner.Inlines.Count > 0)
                combined.AddChild(new ParagraphNode { Text = string.Empty, Inlines = inner.Inlines });
            foreach (var child in inner.Children)
            {
                if (child is BlockNode block) combined.AddChild(block);
            }

            for (var i = 1; i < blocks.Count; i++) combined.AddChild(blocks[i]);
            admonition = combined;
            return true;
        }

        if (blocks[0] is not ParagraphNode first || first.Inlines.Count == 0) return false;
        if (first.Inlines[0] is not TextInlineNode text) return false;

        var match = AdmonitionPrefix.Match(text.Value);
        if (!match.Success) return false;

        _ctx.Report.Tables++;
        _ctx.Report.Count(mapped: true);

        var body = new List<InlineNode>();
        var remainder = text.Value.Substring(match.Length);
        if (remainder.Length > 0) body.Add(new TextInlineNode { Value = remainder });
        for (var i = 1; i < first.Inlines.Count; i++) body.Add(first.Inlines[i]);

        var node = new AdmonitionNode
        {
            AdmonitionType = match.Groups[1].Value.ToUpperInvariant(),
            Inlines = BlockConverter.TrimOuterWhitespace(body),
        };

        // Additional paragraphs in the box become children of the admonition,
        // which the emitter renders as the [TYPE] + ==== block form.
        for (var i = 1; i < blocks.Count; i++) node.AddChild(blocks[i]);

        if (node.Children.Count > 0)
        {
            // The block form has no inline slot, so the first paragraph joins
            // the children instead.
            var rebuilt = new AdmonitionNode { AdmonitionType = node.AdmonitionType };
            rebuilt.AddChild(new ParagraphNode { Text = string.Empty, Inlines = body });
            for (var i = 1; i < blocks.Count; i++) rebuilt.AddChild(blocks[i]);
            admonition = rebuilt;
        }
        else
        {
            admonition = node;
        }

        return true;
    }

    private TableCellNode ConvertCell(XElement tc, int colSpan, int rowSpan)
    {
        _ctx.Report.TableCells++;
        _ctx.Report.Count(mapped: true);

        var tcPr = tc.Element(Ns.W + "tcPr");
        var blocks = ConvertCellBlocks(tc);
        var alignment = ReadAlignment(tc);
        var verticalAlignment = tcPr?.Element(Ns.W + "vAlign").WVal() switch
        {
            "center" => (TableVerticalAlignment?)TableVerticalAlignment.Middle,
            "bottom" => TableVerticalAlignment.Bottom,
            _ => null,
        };

        // The common case — one paragraph of inline content — becomes a plain
        // cell. Anything richer needs an AsciiDoc-styled cell so the nested
        // blocks survive.
        if (blocks.Count == 1 && blocks[0] is ParagraphNode paragraph && paragraph.Children.Count == 0)
        {
            return new TableCellNode
            {
                Text = string.Empty,
                Inlines = paragraph.Inlines,
                ColSpan = colSpan,
                RowSpan = rowSpan,
                Alignment = alignment,
                VerticalAlignment = verticalAlignment,
            };
        }

        return new TableCellNode
        {
            Text = EmitCellSource(blocks),
            ColSpan = colSpan,
            RowSpan = rowSpan,
            Alignment = alignment,
            VerticalAlignment = verticalAlignment,
            ContentStyle = blocks.Count == 0 ? TableCellStyle.Default : TableCellStyle.AsciiDoc,
        };
    }

    private List<BlockNode> ConvertCellBlocks(XElement tc)
    {
        var container = new DocumentNode();
        var converter = new BlockConverter(_ctx, container, nested: true);
        converter.ConvertBody(tc);

        var blocks = new List<BlockNode>();
        foreach (var child in container.Children)
        {
            if (child is BlockNode block) blocks.Add(block);
        }

        return blocks;
    }

    /// <summary>
    /// Emits cell blocks as AsciiDoc source for an <c>a|</c> cell. Nested
    /// tables are re-delimited with <c>!</c>, which is how AsciiDoc nests a
    /// table inside a cell.
    /// </summary>
    private string EmitCellSource(List<BlockNode> blocks)
    {
        var emitter = new AsciidocEmitter();
        var sb = new StringBuilder();

        for (var i = 0; i < blocks.Count; i++)
        {
            var source = emitter.Emit(blocks[i]);
            if (blocks[i] is TableNode)
            {
                source = ToNestedTableSyntax(source);
                _ctx.Report.Approximated("table.nested",
                    "Nested table re-delimited with '!' separators; AsciiDoc supports only one nesting level.");
            }

            sb.Append(source.TrimEnd('\n'));
            if (i < blocks.Count - 1) sb.Append("\n\n");
        }

        return sb.ToString();
    }

    private static string ToNestedTableSyntax(string source)
    {
        var lines = source.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.StartsWith("|===", StringComparison.Ordinal))
                lines[i] = "!===" + line.Substring(4);
            else if (line.StartsWith("|", StringComparison.Ordinal))
                lines[i] = "!" + line.Substring(1).Replace("|", "!");
        }

        return string.Join("\n", lines);
    }

    private static TableAlignment? ReadAlignment(XElement tc)
    {
        foreach (var paragraph in tc.Elements(Ns.W + "p"))
        {
            var jc = paragraph.Element(Ns.W + "pPr")?.Element(Ns.W + "jc").WVal();
            return jc switch
            {
                "center" => TableAlignment.Center,
                "right" or "end" => TableAlignment.Right,
                _ => null,
            };
        }

        return null;
    }

    /// <summary>
    /// Number of rows a vertically merged cell covers, counting the
    /// continuation cells that sit at the same grid column below it.
    /// </summary>
    private static int MeasureRowSpan(List<XElement> rows, int startRow, int gridColumn)
    {
        var span = 1;
        for (var r = startRow + 1; r < rows.Count; r++)
        {
            var column = 0;
            var continues = false;

            foreach (var tc in rows[r].Elements(Ns.W + "tc"))
            {
                var tcPr = tc.Element(Ns.W + "tcPr");
                var colSpan = ReadInt(tcPr?.Element(Ns.W + "gridSpan").WVal()) ?? 1;
                if (column == gridColumn)
                {
                    var vMerge = tcPr?.Element(Ns.W + "vMerge");
                    continues = vMerge is not null && (vMerge.WVal() ?? "continue") != "restart";
                    break;
                }

                column += colSpan;
            }

            if (!continues) break;
            span++;
        }

        return span;
    }

    private static bool HasHeaderRow(List<XElement> rows)
    {
        var repeated = rows[0].Element(Ns.W + "trPr")?.Element(Ns.W + "tblHeader");
        if (repeated.IsToggleOn()) return true;

        // Word documents that never set tblHeader still mark the header row by
        // making every cell bold; require more than one row so a single-row
        // table of bold text is not mistaken for a header-only table.
        if (rows.Count < 2) return false;

        var sawText = false;
        foreach (var tc in rows[0].Elements(Ns.W + "tc"))
        {
            foreach (var run in tc.Descendants(Ns.W + "r"))
            {
                var hasText = false;
                foreach (var t in run.Elements(Ns.W + "t"))
                {
                    if (t.Value.Trim().Length > 0) { hasText = true; break; }
                }

                if (!hasText) continue;
                sawText = true;

                var bold = run.Element(Ns.W + "rPr")?.Element(Ns.W + "b");
                if (!bold.IsToggleOn()) return false;
            }
        }

        return sawText;
    }

    private static IReadOnlyList<TableColumnSpec>? ReadColumns(XElement tbl)
    {
        var grid = tbl.Element(Ns.W + "tblGrid");
        if (grid is null) return null;

        var widths = new List<int>();
        foreach (var col in grid.Elements(Ns.W + "gridCol"))
        {
            var w = ReadInt(col.Attribute(Ns.W + "w")?.Value);
            if (w is null || w <= 0) return null;
            widths.Add(w.Value);
        }

        if (widths.Count == 0) return null;

        // AsciiDoc column widths are proportional integers; reduce the twip
        // values by their GCD so `cols="1,2"` comes out of a 2:1 grid.
        var divisor = widths[0];
        foreach (var width in widths) divisor = Gcd(divisor, width);
        if (divisor <= 0) divisor = 1;

        var specs = new List<TableColumnSpec>(widths.Count);
        foreach (var width in widths)
        {
            specs.Add(new TableColumnSpec { Width = Math.Max(1, width / divisor) });
        }

        return specs;
    }

    private void ReportUnsupportedTableProperties(XElement tbl)
    {
        var tblPr = tbl.Element(Ns.W + "tblPr");
        if (tblPr is null) return;

        if (tblPr.Element(Ns.W + "tblStyle") is not null || tblPr.Element(Ns.W + "tblBorders") is not null)
        {
            _ctx.Report.Add(DocxIssueSeverity.Info, "table.style-dropped",
                "Table borders, shading and banding come from the AsciiDoc backend's stylesheet, not the document.");
        }
    }

    private static int? ReadInt(string? value)
        => value is not null && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            var t = b;
            b = a % b;
            a = t;
        }

        return Math.Abs(a);
    }
}
