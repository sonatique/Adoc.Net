using System.Globalization;
using AdocNet.Ast;

namespace AdocNet.Converters.Pdf;

// Title page, laid out like asciidoctor-pdf's ink_title_page: background image,
// logo, then title / subtitle / authors / revision blocks flowing down from the
// title's "top", each with Prawn's line metrics (see TitlePageBlock).
public sealed partial class PdfRenderer
{
    private PdfTitlePageOptions? _titlePage;
    private float _baseLineHeight = 1.35f;
    private readonly Dictionary<string, string> _fontKeysByPath = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when the document asks for a title page (asciidoctor-pdf rules).</summary>
    private static bool WantsTitlePage(DocumentNode document) =>
        document.Title is not null
        && !document.Attributes.ContainsKey("notitle")
        && (document.Attributes.ContainsKey("title-page")
            || (document.Attributes.TryGetValue("doctype", out var dt) && dt == "book"));

    /// <summary>Doctitle with hard line breaks (" +\n") flattened, for outlines and running content.</summary>
    internal static string PlainTitle(string title) => title.Replace(" +\n", " ").Replace("\n", " ");

    /// <summary>Splits the doctitle into main title and subtitle at its last ": " (Asciidoctor's partition).</summary>
    private static (string Main, string? Subtitle) PartitionTitle(DocumentNode document, string title)
    {
        var sep = (document.Attributes.TryGetValue("title-separator", out var s) && s.Length > 0 ? s : ":") + " ";
        int at = title.LastIndexOf(sep, StringComparison.Ordinal);
        return at < 0 ? (title, null) : (title[..at], title[(at + sep.Length)..]);
    }

    private void RenderTitlePage(PdfWriter w, DocumentNode document)
    {
        var tp = _titlePage ?? new PdfTitlePageOptions();
        w.EnsurePage();
        // No running content on the title page.
        if (w.RunningContentStartPage <= w.CurrentPageNumber)
            w.RunningContentStartPage = w.CurrentPageNumber + 1;

        float pageHeight = w.PageHeight;
        float left = w.MarginLeftValue, width = w.ContentWidth, pageWidth = w.PageWidth;
        float contentTop = pageHeight - w.MarginTopValue;
        float contentHeight = pageHeight - w.MarginTopValue - w.MarginBottomValue;

        // Theme "top": points below the content top, % of content height, or vh from the page top.
        float ResolveTop(string top)
        {
            top = top.Trim();
            if (top.EndsWith("vh", StringComparison.Ordinal)
                && float.TryParse(top[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vh))
                return pageHeight * (1 - vh / 100f);
            if (top.EndsWith("%", StringComparison.Ordinal)
                && float.TryParse(top[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                return contentTop - contentHeight * pct / 100f;
            return contentTop - (PdfThemeLoader.ParseLength(top) ?? 0f);
        }

        if (tp.BackgroundImagePath is { } bg)
            DrawImageFile(w, bg, 0, 0, pageWidth, pageHeight);

        if (tp.LogoImagePath is { } logo && TryReadImageSize(logo, out var lw, out var lh))
        {
            float drawW = Math.Min(tp.LogoWidth ?? lw, width);
            float drawH = drawW * lh / lw;
            float x = (tp.LogoAlignment ?? tp.Alignment) switch
            {
                PdfAlignment.Right => left + width - drawW,
                PdfAlignment.Center => left + (width - drawW) / 2,
                _ => left,
            };
            float top = ResolveTop(tp.LogoTop);
            DrawImageFile(w, logo, x, top - drawH, drawW, drawH);
        }

        var (main, subtitle) = PartitionTitle(document, document.Title!);
        float y = tp.Title.Top is { } titleTop ? ResolveTop(titleTop) : contentTop;
        y = TitlePageBlock(w, main, tp.Title, tp.Alignment, y);
        if (subtitle is not null)
            y = TitlePageBlock(w, subtitle, tp.Subtitle, tp.Alignment, y);

        if (document.Attributes.TryGetValue("author", out var author) && !string.IsNullOrWhiteSpace(author))
        {
            var authors = new List<string> { author };
            for (int i = 2; document.Attributes.TryGetValue($"author_{i}", out var more); i++) authors.Add(more);
            y = TitlePageBlock(w, string.Join(", ", authors), tp.Authors, tp.Alignment, y);
        }

        var revision = new List<string>();
        if (document.Attributes.TryGetValue("revnumber", out var rev) && rev.Length > 0)
        {
            var label = document.Attributes.TryGetValue("version-label", out var vl) ? vl : "Version";
            revision.Add(label.Length > 0 ? $"{label} {rev}" : rev);
        }
        if (document.Attributes.TryGetValue("revdate", out var revdate) && revdate.Length > 0) revision.Add(revdate);
        if (revision.Count > 0)
        {
            var text = string.Join(", ", revision);
            if (document.Attributes.TryGetValue("revremark", out var remark) && remark.Length > 0) text += ": " + remark;
            TitlePageBlock(w, text, tp.Revision, tp.Alignment, y);
        }

        RestoreBodyFill(w);
    }

    /// <summary>
    /// Draws one text block whose top edge is at <paramref name="top"/> (PDF y) and returns the
    /// y below it. Uses Prawn's metrics: leading = (lineHeight - 1) * size, half of it plus the
    /// font's line gap above the first line, ascender to the first baseline, and
    /// (ascender - descender + lineGap) * size + leading between baselines. " +\n" is a hard break.
    /// </summary>
    private float TitlePageBlock(PdfWriter w, string text, PdfTitlePageText style, PdfAlignment align, float top)
    {
        string font = style.FontPath is { } path ? FontKeyForPath(w, path)
            : style.Bold ? _fontBold : style.Italic ? _fontItalic : _fontRegular;
        float size = style.FontSize ?? _bodyFontSize;
        var (asc, desc, gap) = FontMetrics(w, font);
        float leading = ((style.LineHeight ?? _baseLineHeight) - 1f) * size;

        var color = style.Color ?? _bodyColor;
        if (color is { } c) w.SetFillColor(c.R, c.G, c.B); else w.SetFillColor(0, 0, 0);

        float y = top - style.MarginTop;
        float baseline = y - (leading / 2 + gap * size) - asc * size;
        bool first = true;
        foreach (var segment in text.Split(new[] { " +\n", "\n" }, StringSplitOptions.None))
        {
            foreach (var line in w.WrapText(segment.Trim(), font, size, w.ContentWidth))
            {
                if (!first) baseline -= (asc + desc + gap) * size + leading;
                first = false;
                float lineWidth = w.MeasureText(line, font, size);
                float x = align switch
                {
                    PdfAlignment.Right => w.MarginLeftValue + w.ContentWidth - lineWidth,
                    PdfAlignment.Center => w.MarginLeftValue + (w.ContentWidth - lineWidth) / 2,
                    _ => w.MarginLeftValue,
                };
                w.WriteText(line, font, size, x, baseline);
            }
        }
        return baseline - desc * size - leading / 2 - style.MarginBottom;
    }

    /// <summary>Ascender, |descender| and line gap per em for a font key (AFM values for standard fonts).</summary>
    private static (float Asc, float Desc, float Gap) FontMetrics(PdfWriter w, string fontKey)
    {
        if (w.GetEmbeddedFont(w.ResolveFontKey(fontKey)) is { } f)
        {
            float em = f.UnitsPerEm;
            return (f.Ascender / em, Math.Abs(f.Descender) / em, f.LineGap / em);
        }
        return (0.718f, 0.207f, 0f); // Helvetica
    }

    private string FontKeyForPath(PdfWriter w, string path)
    {
        if (_fontKeysByPath.TryGetValue(path, out var key)) return key;
        key = _fontRegular;
        if (File.Exists(path))
        {
            try { key = w.RegisterEmbeddedFont($"TP{_fontKeysByPath.Count}", TrueTypeFont.Parse(File.ReadAllBytes(path))); }
            catch (Exception ex) when (ex is IOException or IndexOutOfRangeException or ArgumentException) { }
        }
        _fontKeysByPath[path] = key;
        return key;
    }

    private static bool TryReadImageSize(string path, out float width, out float height)
    {
        width = height = 0;
        if (!File.Exists(path)) return false;
        var data = File.ReadAllBytes(path);
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            if (SvgParser.Parse(data) is not { } svg) return false;
            (width, height) = (svg.ViewBoxWidth, svg.ViewBoxHeight);
        }
        else
        {
            if ((ImageParser.TryParseJpeg(data) ?? ImageParser.TryParsePng(data)) is not { } img) return false;
            (width, height) = (img.Width, img.Height);
        }
        return width > 0 && height > 0;
    }

    private static void DrawImageFile(PdfWriter w, string path, float x, float y, float width, float height)
    {
        if (!File.Exists(path)) return;
        var data = File.ReadAllBytes(path);
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            if (SvgParser.Parse(data) is { } svg) w.DrawSvg(svg, x, y, width, height);
        }
        else if ((ImageParser.TryParseJpeg(data) ?? ImageParser.TryParsePng(data)) is { } img)
        {
            w.DrawImage(w.EmbedImage(img), x, y, width, height);
        }
    }
}
