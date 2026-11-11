using AdocNet.Ast;

namespace AdocNet.Converters.Pdf;

// Vertical metrics as asciidoctor-pdf (Prawn) computes them, used when the body font is
// an embedded TrueType font (themed output). For a font at size s and line height lh:
//   leading      = (lh - 1) * s
//   advance      = (ascender - descender + lineGap) * s + leading     (baseline to baseline)
//   first offset = leading / 2 + lineGap * s + ascender * s           (block top to first baseline)
//   bottom       = |descender| * s + leading / 2                      (last baseline to block bottom)
// Blocks stack top-to-bottom with their theme margins in between; heading margin-top is
// dropped at the top of a page. The writer's cursor stays "the next body baseline", so a
// block top is always cursor + body first offset.
public sealed partial class PdfRenderer
{
    private bool _prawnMetrics;
    private float _bodyFirstOffset;
    private float _headingLineHeight = 1f;
    private float _codeLineHeight = 1.25f;
    private float _codePadding = 11f;
    private float _tocLineHeight = 1.4f;
    private float _listIndent = 18f;
    private float _tableLineHeight = 1.15f;
    private float[] _tableCellPadding = { 3, 3, 3, 3 };
    private float _tableGridWidth = 0.5f;
    private float _tableHeadBorderBottomWidth = 1.25f;
    private float _tocIndent = 12f;
    private PdfColor _tocDotLeaderColor = new(169 / 255f, 169 / 255f, 169 / 255f);
    private readonly float[] _headingMarginTops = new float[4];
    private readonly float[] _headingMarginBottoms = new float[4];

    private Action? _repeatHeaderAfterBreak;

    // prawn-table hands cell text 1pt of slack (Prawn::Table::Cell::FPTolerance).
    private float CellWrapTolerance => _prawnMetrics ? 1f : 0f;

    private readonly record struct LineMetrics(float Advance, float FirstOffset, float Bottom);

    private static LineMetrics MetricsFor(PdfWriter w, string fontKey, float size, float lineHeight)
    {
        var (asc, desc, gap) = FontMetrics(w, fontKey);
        float leading = (lineHeight - 1f) * size;
        return new LineMetrics(
            (asc + desc + gap) * size + leading,
            leading / 2 + gap * size + asc * size,
            desc * size + leading / 2);
    }

    private void ApplyPrawnLineMetrics(PdfWriter writer, PdfRenderOptions options)
    {
        _prawnMetrics = _hasTrueTypeBodyFont;
        if (!_prawnMetrics)
        {
            writer.FirstLineOffset = null;
            writer.VerbatimWrapAtSpaces = false;
            return;
        }

        var body = MetricsFor(writer, _fontRegular, _bodyFontSize, options.LineSpacing);
        _bodyLeading = body.Advance;
        _bodyFirstOffset = body.FirstOffset;
        writer.BodyLeading = body.Advance;
        writer.FirstLineOffset = body.FirstOffset;
        writer.VerbatimWrapAtSpaces = true;
        _codeLineHeight = options.CodeLineHeight ?? 1.25f;
        _codeLeading = MetricsFor(writer, _fontMono, _codeFontSize, _codeLineHeight).Advance;
        _codePadding = options.CodePadding ?? 11f;
        _tocLineHeight = options.TocLineHeight ?? 1.4f;
        _listIndent = options.ListIndent ?? 18f;
        _tableLineHeight = options.TableLineHeight ?? options.LineSpacing;
        _tableCellPadding = options.TableCellPadding is { Length: 4 } pad ? pad : new float[] { 3, 3, 3, 3 };
        _tableGridWidth = options.TableGridWidth ?? 0.5f;
        _tableHeadBorderBottomWidth = options.TableHeadBorderBottomWidth ?? 1.25f;
        _tocIndent = options.TocIndent ?? 12f;
        _tocDotLeaderColor = options.TocDotLeaderColor ?? new PdfColor(169 / 255f, 169 / 255f, 169 / 255f);
        _headingLineHeight = options.HeadingLineHeight ?? 1f;

        float defaultTop = options.HeadingMarginTop ?? 4.8f;
        float defaultBottom = options.HeadingMarginBottom ?? 10.8f;
        float?[] tops = { options.Heading2MarginTop, options.Heading3MarginTop, options.Heading4MarginTop, options.Heading5MarginTop };
        float?[] bottoms = { options.Heading2MarginBottom, options.Heading3MarginBottom, options.Heading4MarginBottom, options.Heading5MarginBottom };
        for (int i = 0; i < 4; i++)
        {
            _headingMarginTops[i] = tops[i] ?? defaultTop;
            _headingMarginBottoms[i] = bottoms[i] ?? defaultBottom;
        }
    }

    /// <summary>Top edge of the next block, from the cursor (next body baseline).</summary>
    private float NextBlockTop(PdfWriter w) => w.CursorY + _bodyFirstOffset;

    /// <summary>Moves the cursor so the next block's top edge is <paramref name="top"/>.</summary>
    private void SetNextBlockTop(PdfWriter w, float top) => w.SetCursor(top - _bodyFirstOffset);

    private void WritePrawnHeading(PdfWriter w, List<TextSegment> segments, float fontSize, int level, Action register)
    {
        int i = Math.Min(Math.Max(level, 1), 4) - 1;
        var m = MetricsFor(w, _fontHeading, fontSize, _headingLineHeight);

        float top = NextBlockTop(w);
        if (w.PageHasContent)
            top -= _headingMarginTops[i];
        // Keep the heading with at least one line of what follows.
        float needed = m.FirstOffset + m.Bottom + _headingMarginBottoms[i] + _bodyLeading;
        if (w.PageHasContent && top - needed < w.MarginBottomValue)
        {
            w.FinishPage();
            w.StartPage();
            top = NextBlockTop(w);
        }

        w.SetCursor(top - m.FirstOffset);
        register();
        w.WriteWrappedSegments(segments, m.Advance);
        float lastBaseline = w.CursorY + m.Advance;
        SetNextBlockTop(w, lastBaseline - m.Bottom - _headingMarginBottoms[i]);
    }

    /// <summary>
    /// Text-label admonition as asciidoctor-pdf lays it out without icons: padding
    /// [4, 12, 4, 12], the bold label vertically centred in its column, a 0.5pt #EEEEEE
    /// column rule, and the content 12pt right of the rule.
    /// </summary>
    private void RenderPrawnTextAdmonition(PdfWriter w, AdmonitionNode admonition, int indentLevel, FootnoteState footnotes)
    {
        const float padV = 4f, padH = 12f, ruleWidth = 0.5f;
        var label = admonition.AdmonitionType.ToUpperInvariant();
        float left = w.MarginLeftValue;
        float ruleX = left + padH + w.MeasureText(label, _fontBold, _bodyFontSize) + padH;
        var body = MetricsFor(w, _fontRegular, _bodyFontSize, _baseLineHeight);

        float top = NextBlockTop(w);
        if (top - (2 * padV + body.FirstOffset + body.Bottom) < w.MarginBottomValue && w.PageHasContent)
        {
            w.FinishPage();
            w.StartPage();
            top = NextBlockTop(w);
        }

        SetNextBlockTop(w, top - padV);
        float savedIndent = w.PushIndent(ruleX - w.MarginLeftBase + padH);
        float savedRight = w.ContentRightIndent;
        w.ContentRightIndent = savedRight + padH;
        float contentBottom;
        if (admonition.Children.Count > 0)
        {
            foreach (var child in admonition.Children)
                RenderBlock(w, child, indentLevel, footnotes);
            // The last block's margin-bottom does not apply inside the box.
            contentBottom = NextBlockTop(w) + _paragraphSpacingAfter;
        }
        else
        {
            var segments = BuildInlineSegments(admonition.Inlines, admonition.Text ?? string.Empty,
                _fontRegular, _bodyFontSize, footnotes);
            w.WriteWrappedSegments(segments, _bodyLeading);
            contentBottom = w.CursorY + _bodyLeading - body.Bottom;
        }
        w.ContentRightIndent = savedRight;
        w.PopIndent(savedIndent);

        float bottom = contentBottom - padV;
        w.SetStrokeColor(0xEE / 255f, 0xEE / 255f, 0xEE / 255f);
        w.DrawLine(ruleX, top, ruleX, bottom, ruleWidth);
        w.SetStrokeColor(0, 0, 0);

        // Label: one line, centred on the box using the font height (asc + desc + gap).
        var (asc, desc, gap) = FontMetrics(w, _fontBold);
        float height = (asc + desc + gap) * _bodyFontSize;
        float labelTop = (top + bottom) / 2 + height / 2;
        RestoreBodyFill(w);
        w.WriteText(label, _fontBold, _bodyFontSize, left + padH, labelTop - (gap + asc) * _bodyFontSize);

        SetNextBlockTop(w, bottom - _paragraphSpacingAfter);
    }

    /// <summary>
    /// Table row laid out like prawn-table: each cell's inline content as styled segments
    /// (codespans in the mono font, bold, links...), wrapped to the cell width plus prawn's 1pt
    /// tolerance; each line sized by the largest ascender, descender and gap of the fonts on it;
    /// the row as tall as its tallest cell plus the cell padding.
    /// </summary>
    private void RenderPrawnTableRow(PdfWriter w, List<PlacedCell> placed, float[] colWidths, float cellPadding,
        string font, float fontSize, IReadOnlyList<TableColumnSpec>? columns, TableGrid gridOpts,
        bool isFirstRow, bool isLastRow, bool isHeaderRow, PdfColor? headerBackground, PdfColor? headerTextColor)
    {
        float leading = (_tableLineHeight - 1f) * fontSize;
        var noFootnotes = new FootnoteState();
        var cells = new List<(PlacedCell Cell, List<List<TextSegment>> Lines, List<(float Asc, float Desc, float Gap)> Metrics,
            float X, float Width, TableAlignment? Align, float Height)>();

        foreach (var p in placed)
        {
            float x = w.MarginLeftValue;
            for (int c = 0; c < p.Col && c < colWidths.Length; c++) x += colWidths[c];
            float width = 0;
            for (int c = 0; c < p.ColSpan && p.Col + c < colWidths.Length; c++) width += colWidths[p.Col + c];

            List<TextSegment> segments;
            if (p.HasFootnote)
            {
                segments = new List<TextSegment>();
                foreach (var run in p.Runs)
                    segments.Add(run.IsMarker
                        ? new TextSegment(run.Text, _fontRegular, fontSize * PdfWriter.SuperscriptScale,
                            LinkUri: run.LinkUri, Superscript: true, DestId: run.DestId)
                        : new TextSegment(run.Text, font, fontSize));
            }
            else
            {
                segments = BuildInlineSegments(p.Cell.Inlines, p.Text, font, fontSize, noFootnotes);
            }
            segments = w.ExpandSegmentsForFallback(segments);
            var lines = w.WrapSegments(segments, width - 2 * cellPadding + CellWrapTolerance);
            if (lines.Count == 0) lines.Add(new List<TextSegment>());

            // prawn-table cell (measured with Noto Serif, line gap 0, and Calibri, gap 0.221 em):
            // first baseline = top + the cell font's line gap + the line's ascender; each following
            // line adds its height (max of the fonts' ascender - descender + gap) plus leading;
            // content height = that gap + the line heights. Metrics[i] = (ascender, descender, height).
            var metrics = new List<(float, float, float)>();
            float cellGap = FontMetrics(w, font).Gap * fontSize;
            float height = cellGap;
            for (int li = 0; li < lines.Count; li++)
            {
                var line = lines[li];
                float asc = 0, desc = 0, lineHeight = 0;
                foreach (var seg in line.Count > 0 ? line : new List<TextSegment> { new("", font, fontSize) })
                {
                    var (a, d, g) = FontMetrics(w, seg.Font);
                    asc = Math.Max(asc, a * seg.FontSize);
                    desc = Math.Max(desc, d * seg.FontSize);
                    lineHeight = Math.Max(lineHeight, (a + d + g) * seg.FontSize);
                }
                metrics.Add((asc, desc, lineHeight));
                height += lineHeight + (li == 0 ? 0 : leading);
            }

            var align = p.Cell.Alignment;
            if (align is null && columns is not null && p.Col < columns.Count)
                align = columns[p.Col].Alignment;
            cells.Add((p, lines, metrics, x, width, align, height));
        }

        float rowHeight = _tableCellPadding[0] + (cells.Count > 0 ? cells.Max(c => c.Height) : 0) + _tableCellPadding[2];
        float rowTop = NextBlockTop(w);
        if (rowTop - rowHeight < w.MarginBottomValue && w.PageHasContent)
        {
            w.FinishPage();
            w.StartPage();
            if (_repeatHeaderAfterBreak is { } repeat)
            {
                _repeatHeaderAfterBreak = null; // the header row itself must not recurse
                repeat();
            }
            rowTop = NextBlockTop(w);
        }
        float rowBottom = rowTop - rowHeight;

        if (headerBackground is { } hbg)
        {
            w.SetFillColor(hbg.R, hbg.G, hbg.B);
            w.DrawRect(w.MarginLeftValue, rowBottom, w.ContentWidth, rowHeight, fill: true);
        }
        if (headerTextColor is { } htc) w.SetFillColor(htc.R, htc.G, htc.B);
        else RestoreBodyFill(w);

        float firstGap = FontMetrics(w, font).Gap * fontSize;
        foreach (var (_, lines, metrics, x, width, align, _) in cells)
        {
            float baseline = rowTop - _tableCellPadding[0] - firstGap;
            for (int li = 0; li < lines.Count; li++)
            {
                var (asc, _, lineHeight) = metrics[li];
                baseline -= li == 0 ? asc : lineHeight + leading;
                var line = lines[li];
                float lineWidth = 0;
                foreach (var seg in line) lineWidth += w.MeasureText(seg.Text, seg.Font, seg.FontSize);
                float textX = align switch
                {
                    TableAlignment.Right => x + width - cellPadding - lineWidth,
                    TableAlignment.Center => x + (width - lineWidth) / 2,
                    _ => x + cellPadding,
                };
                if (line.Count > 0) w.WriteTextSegments(line, textX, baseline);
            }
        }
        if (headerTextColor is not null) RestoreBodyFill(w);

        SetNextBlockTop(w, rowBottom);
        DrawRowBorders(w, placed, colWidths, rowTop, rowBottom, gridOpts, isFirstRow, isLastRow, isHeaderRow);
    }
}
