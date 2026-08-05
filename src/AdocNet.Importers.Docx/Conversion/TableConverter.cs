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

        var rowNodes = new List<TableRowNode>(rows.Count);
        var alignments = new List<TableAlignment?>();
        var alignmentConflict = new List<bool>();
        var spanned = false;

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
                if (colSpan > 1 || rowSpan > 1) spanned = true;

                // Alignment is recorded per column rather than per cell: a
                // per-cell alignment specifier has to sit between two cell
                // separators, where it is indistinguishable from content, so
                // it goes into the `cols` attribute instead.
                if (colSpan == 1) RecordAlignment(alignments, alignmentConflict, gridColumn, ReadAlignment(tc));

                rowNode.AddChild(ConvertCell(tc, colSpan, rowSpan));
                gridColumn += colSpan;
            }

            if (rowNode.Children.Count > 0) rowNodes.Add(rowNode);
        }

        var columns = BuildColumns(tbl, rowNodes, alignments, alignmentConflict, spanned);

        var table = new TableNode
        {
            HasHeader = HasHeaderRow(rows),
            Columns = columns,
        };

        foreach (var row in rowNodes) table.AddChild(row);
        return table;
    }

    private static void RecordAlignment(List<TableAlignment?> alignments, List<bool> conflict,
        int column, TableAlignment? alignment)
    {
        while (alignments.Count <= column)
        {
            alignments.Add(null);
            conflict.Add(false);
        }

        if (conflict[column]) return;

        if (alignments[column] is TableAlignment existing)
        {
            if (existing != (alignment ?? TableAlignment.Left)) conflict[column] = true;
            return;
        }

        alignments[column] = alignment ?? TableAlignment.Left;
    }

    /// <summary>
    /// Column specs for the table: grid widths when they are not uniform, and
    /// a column-wide alignment when every cell in the column agrees. An
    /// explicit spec is also emitted whenever the table has a spanned cell, so
    /// the column count never has to be inferred from the first row.
    /// </summary>
    private static IReadOnlyList<TableColumnSpec>? BuildColumns(XElement tbl, List<TableRowNode> rows,
        List<TableAlignment?> alignments, List<bool> conflict, bool spanned)
    {
        var widths = ReadColumns(tbl);
        var hasAlignment = false;
        foreach (var alignment in alignments)
        {
            if (alignment is not null && alignment != TableAlignment.Left) { hasAlignment = true; break; }
        }

        if (widths is null && !spanned && !hasAlignment) return null;

        var count = widths?.Count ?? CountGridColumns(tbl) ?? WidestRow(rows);
        if (count <= 0) return null;

        var columns = new List<TableColumnSpec>(count);
        for (var i = 0; i < count; i++)
        {
            var alignment = i < alignments.Count && !conflict[i]
                ? alignments[i] ?? TableAlignment.Left
                : TableAlignment.Left;

            columns.Add(new TableColumnSpec
            {
                Width = widths is not null && i < widths.Count ? widths[i].Width : 1,
                Alignment = alignment,
            });
        }

        return columns;
    }

    private static int? CountGridColumns(XElement tbl)
    {
        var grid = tbl.Element(Ns.W + "tblGrid");
        if (grid is null) return null;

        var count = 0;
        foreach (var _ in grid.Elements(Ns.W + "gridCol")) count++;
        return count > 0 ? count : null;
    }

    /// <summary>Widest row measured in grid columns, spans included.</summary>
    private static int WidestRow(List<TableRowNode> rows)
    {
        var widest = 0;
        foreach (var row in rows)
        {
            var width = 0;
            foreach (var cell in row.Children)
            {
                if (cell is TableCellNode c) width += c.ColSpan;
            }

            widest = Math.Max(widest, width);
        }

        return widest;
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

        var blocks = ConvertCellBlocks(tc);

        // The common case — one paragraph of inline content — becomes a plain
        // cell. Anything richer needs an AsciiDoc-styled cell so the nested
        // blocks survive. Alignment is carried by the column spec, not the
        // cell, because a per-cell specifier sits between two separators where
        // it cannot be told apart from content.
        if (blocks.Count == 1 && blocks[0] is ParagraphNode paragraph && paragraph.Children.Count == 0)
        {
            return new TableCellNode
            {
                Text = string.Empty,
                Inlines = NeutraliseCellSpecLookalike(paragraph.Inlines),
                ColSpan = colSpan,
                RowSpan = rowSpan,
            };
        }

        return new TableCellNode
        {
            Text = EmitCellSource(blocks),
            ColSpan = colSpan,
            RowSpan = rowSpan,
            ContentStyle = blocks.Count == 0 ? TableCellStyle.Default : TableCellStyle.AsciiDoc,
        };
    }

    /// <summary>
    /// A cell holding nothing but a style letter is read as the <em>next</em>
    /// cell's specifier — <c>|a|b</c> is an empty cell followed by an
    /// AsciiDoc-styled cell, not the two cells "a" and "b". Wrapping such a
    /// cell's text in a passthrough keeps it as content.
    /// </summary>
    private static IReadOnlyList<InlineNode> NeutraliseCellSpecLookalike(IReadOnlyList<InlineNode> inlines)
    {
        if (inlines.Count != 1 || inlines[0] is not TextInlineNode text) return inlines;

        var value = text.Value.Trim();
        if (value.Length != 1 || "adehlms".IndexOf(value[0]) < 0) return inlines;

        return new List<InlineNode> { new TextInlineNode { Value = "+++" + text.Value + "+++" } };
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

        var total = 0L;
        foreach (var width in widths) total += width;
        if (total <= 0) return null;

        // Equal columns are AsciiDoc's default; emitting explicit widths for
        // them adds noise without changing the rendering.
        var even = true;
        foreach (var width in widths)
        {
            if (Math.Abs(width - (double)total / widths.Count) > total * 0.01) { even = false; break; }
        }

        if (even) return null;

        // AsciiDoc column widths are proportional integers. Prefer the GCD
        // reduction when it lands on small numbers (a 2:1 grid should read
        // `cols="1,2"`), and fall back to percentages when the twip values are
        // coprime and would otherwise produce four-digit weights.
        var divisor = widths[0];
        foreach (var width in widths) divisor = Gcd(divisor, width);
        if (divisor <= 0) divisor = 1;

        var reduced = new List<int>(widths.Count);
        var largest = 0;
        foreach (var width in widths)
        {
            var value = Math.Max(1, width / divisor);
            largest = Math.Max(largest, value);
            reduced.Add(value);
        }

        if (largest > 12)
        {
            reduced.Clear();
            foreach (var width in widths)
                reduced.Add(Math.Max(1, (int)Math.Round(width * 100.0 / total)));
        }

        var specs = new List<TableColumnSpec>(reduced.Count);
        foreach (var value in reduced) specs.Add(new TableColumnSpec { Width = value });
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
