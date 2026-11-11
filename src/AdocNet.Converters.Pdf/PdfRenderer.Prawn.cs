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
    private readonly float[] _headingMarginTops = new float[4];
    private readonly float[] _headingMarginBottoms = new float[4];

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
            return;
        }

        var body = MetricsFor(writer, _fontRegular, _bodyFontSize, options.LineSpacing);
        _bodyLeading = body.Advance;
        _bodyFirstOffset = body.FirstOffset;
        writer.BodyLeading = body.Advance;
        writer.FirstLineOffset = body.FirstOffset;
        _codeLineHeight = options.CodeLineHeight ?? 1.25f;
        _codeLeading = MetricsFor(writer, _fontMono, _codeFontSize, _codeLineHeight).Advance;
        _codePadding = options.CodePadding ?? 11f;
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

    private void WritePrawnHeading(PdfWriter w, List<TextSegment> segments, float fontSize, int level)
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
        w.WriteWrappedSegments(segments, m.Advance);
        float lastBaseline = w.CursorY + m.Advance;
        SetNextBlockTop(w, lastBaseline - m.Bottom - _headingMarginBottoms[i]);
    }
}
