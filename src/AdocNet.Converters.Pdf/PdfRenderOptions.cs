using AdocNet;

namespace AdocNet.Converters.Pdf;

/// <summary>Text alignment for PDF elements.</summary>
public enum PdfAlignment
{
    /// <summary>Align to the left edge.</summary>
    Left,
    /// <summary>Center horizontally.</summary>
    Center,
    /// <summary>Align to the right edge.</summary>
    Right
}

/// <summary>RGB color for PDF rendering (values 0.0–1.0).</summary>
public readonly record struct PdfColor(float R, float G, float B);

/// <summary>
/// Options controlling PDF rendering: page size, margins, fonts, headers, footers,
/// typography, and visual styling.
/// </summary>
public sealed class PdfRenderOptions : RenderOptions
{
    /// <summary>A shared default instance (A4 page, 1-inch margins, no header/footer).</summary>
    public static new PdfRenderOptions Default { get; } = new();

    // ── Page geometry ────────────────────────────────────────────────────

    /// <summary>Page width in points (72 points = 1 inch). Default: 595 (A4).</summary>
    public float PageWidth { get; init; } = 595f;

    /// <summary>Page height in points. Default: 842 (A4).</summary>
    public float PageHeight { get; init; } = 842f;

    /// <summary>Left margin in points. Default: 72 (1 inch).</summary>
    public float MarginLeft { get; init; } = 72f;

    /// <summary>Right margin in points. Default: 72.</summary>
    public float MarginRight { get; init; } = 72f;

    /// <summary>Top margin in points. Default: 72.</summary>
    public float MarginTop { get; init; } = 72f;

    /// <summary>Bottom margin in points. Default: 72.</summary>
    public float MarginBottom { get; init; } = 72f;

    // ── Font paths ───────────────────────────────────────────────────────

    /// <summary>Path to a TrueType font file (.ttf) for body text. Null = use standard Helvetica.</summary>
    public string? FontPath { get; init; }

    /// <summary>Path to a TrueType font file for bold text. Null = use FontPath or standard Helvetica-Bold.</summary>
    public string? BoldFontPath { get; init; }

    /// <summary>Path to a TrueType font file for italic text. Null = use standard Helvetica-Oblique.</summary>
    public string? ItalicFontPath { get; init; }

    /// <summary>Path to a TrueType font file for monospace text. Null = use standard Courier.</summary>
    public string? MonoFontPath { get; init; }

    /// <summary>Path to a TrueType font file for bold monospace text. Null = use standard Courier-Bold.</summary>
    public string? MonoBoldFontPath { get; init; }

    /// <summary>Path to a TrueType font file for italic monospace text. Null = use standard Courier-Oblique.</summary>
    public string? MonoItalicFontPath { get; init; }

    /// <summary>Path to a TrueType font file for bold-italic monospace text. Null = use standard Courier-BoldOblique.</summary>
    public string? MonoBoldItalicFontPath { get; init; }

    // ── Typography ───────────────────────────────────────────────────────

    /// <summary>Base body text font size in points. Default: 11.</summary>
    public float FontSize { get; init; } = 11f;

    /// <summary>Code block font size in points. Default: 9.</summary>
    public float CodeFontSize { get; init; } = 9f;

    /// <summary>Document title font size in points. Default: 24.</summary>
    public float TitleFontSize { get; init; } = 24f;

    /// <summary>
    /// Heading scale factor. Each heading level is previous × scale.
    /// H2 = TitleFontSize × scale, H3 = H2 × scale, etc. Default: 0.85.
    /// </summary>
    public float HeadingScale { get; init; } = 0.85f;

    /// <summary>Line spacing multiplier. Leading = fontSize × lineSpacing. Default: 1.35.</summary>
    public float LineSpacing { get; init; } = 1.35f;

    /// <summary>Line spacing multiplier for the document title. Null = use LineSpacing.</summary>
    public float? TitleLineHeight { get; init; }

    /// <summary>Enable hyphenation in body text. Default: false.</summary>
    public bool EnableHyphenation { get; init; }

    /// <summary>Spacing before paragraphs in points. Default: 0.</summary>
    public float ParagraphSpacingBefore { get; init; } = 0f;

    /// <summary>Spacing after paragraphs in points. Default: 12 (matches Asciidoctor-pdf).</summary>
    public float ParagraphSpacingAfter { get; init; } = 12f;

    // ── Per-heading-level overrides ─────────────────────────────────────

    /// <summary>Path to a TrueType font for heading text. Null = use body font (FontPath).</summary>
    public string? HeadingFontPath { get; init; }

    /// <summary>Font size for H2 headings (section level 1). Null = calculated from TitleFontSize × HeadingScale.</summary>
    public float? Heading2FontSize { get; init; }

    /// <summary>Font size for H3 headings (section level 2). Null = calculated from H2 × HeadingScale.</summary>
    public float? Heading3FontSize { get; init; }

    /// <summary>Font size for H4 headings (section level 3). Null = calculated from H3 × HeadingScale.</summary>
    public float? Heading4FontSize { get; init; }

    /// <summary>Font size for H5 headings (section level 4). Null = calculated from H4 × HeadingScale.</summary>
    public float? Heading5FontSize { get; init; }

    /// <summary>Margin below H2 headings in points. Null = half of ParagraphSpacingAfter.</summary>
    public float? Heading2MarginBottom { get; init; }

    /// <summary>Margin below H3 headings in points. Null = half of ParagraphSpacingAfter.</summary>
    public float? Heading3MarginBottom { get; init; }

    /// <summary>Margin below H4 headings in points. Null = half of ParagraphSpacingAfter.</summary>
    public float? Heading4MarginBottom { get; init; }

    /// <summary>Margin below H5 headings in points. Null = half of ParagraphSpacingAfter.</summary>
    public float? Heading5MarginBottom { get; init; }

    // ── Headers and footers ──────────────────────────────────────────────

    /// <summary>Show page numbers in footer. Default: false.</summary>
    public bool ShowPageNumbers { get; init; }

    /// <summary>Header text template. Null = no header. Supports {page}, {pages}, {section-title}, {document-title} placeholders.</summary>
    public string? HeaderText { get; init; }

    /// <summary>Footer text template. Null = no footer (unless ShowPageNumbers). Supports {page}, {pages}, {section-title}, {document-title} placeholders.</summary>
    public string? FooterText { get; init; }

    /// <summary>Header font size in points. Default: 9.</summary>
    public float HeaderFontSize { get; init; } = 9f;

    /// <summary>Footer font size in points. Default: 9.</summary>
    public float FooterFontSize { get; init; } = 9f;

    /// <summary>Header text color. Null = black.</summary>
    public PdfColor? HeaderFontColor { get; init; }

    /// <summary>Footer text color. Null = black.</summary>
    public PdfColor? FooterFontColor { get; init; }

    /// <summary>Header text alignment. Default: Center.</summary>
    public PdfAlignment HeaderAlignment { get; init; } = PdfAlignment.Center;

    /// <summary>Footer text alignment. Default: Center.</summary>
    public PdfAlignment FooterAlignment { get; init; } = PdfAlignment.Center;

    /// <summary>Height of the header area in points. Controls vertical positioning of header text. Default: 0 (auto: half of top margin).</summary>
    public float HeaderHeight { get; init; }

    /// <summary>Height of the footer area in points. Controls vertical positioning of footer text. Default: 0 (auto: place at marginBottom - 20).</summary>
    public float FooterHeight { get; init; }

    /// <summary>When to start showing headers/footers. "after-toc" suppresses them on title/TOC pages. Default: null (show on all pages).</summary>
    public string? RunningContentStartAt { get; init; }

    /// <summary>Path to an SVG image to render in the footer area (e.g., a logo). Null = no footer image.</summary>
    public string? FooterImagePath { get; init; }

    /// <summary>Width of the footer image in points. Default: 64.</summary>
    public float FooterImageWidth { get; init; } = 64f;

    // ── Images ───────────────────────────────────────────────────────────

    /// <summary>Base directory for resolving relative image paths. Null = images fall back to placeholder.</summary>
    public string? BaseDirectory { get; init; }

    // ── Syntax highlighting ────────────────────────────────────────────

    /// <summary>
    /// Color scheme for syntax highlighting in source blocks.
    /// Null = no highlighting (plain monospace, beta.3 compatible). Default: null.
    /// Set to <see cref="SyntaxColorScheme.Default"/> to enable highlighting.
    /// </summary>
    public SyntaxColorScheme? SyntaxColors { get; init; }

    // ── Visual styling ───────────────────────────────────────────────────

    /// <summary>Color for hyperlink text. Null = no coloring (black). Default: dark blue (0, 0, 0.8).</summary>
    public PdfColor? LinkColor { get; init; } = new PdfColor(0f, 0f, 0.8f);

    /// <summary>Background color for code blocks. Null = no background. Default: light gray.</summary>
    public PdfColor? CodeBackground { get; init; } = new PdfColor(0.95f, 0.95f, 0.95f);

    /// <summary>Background color for inline codespans. Null = no background (matches Asciidoctor default). Default: null.</summary>
    public PdfColor? CodespanBackground { get; init; }

    /// <summary>Border color for code blocks. Default: light gray (#CCCCCC), matching Asciidoctor-pdf.</summary>
    public PdfColor? CodeBorderColor { get; init; } = new PdfColor(0.8f, 0.8f, 0.8f);

    /// <summary>Left border width for admonition blocks in points. Default: 2.</summary>
    public float AdmonitionBorderWidth { get; init; } = 2f;

    /// <summary>Repeat header row when a table spans pages. Default: true.</summary>
    public bool RepeatTableHeader { get; init; } = true;

    /// <summary>Color for heading text (h1–h5). Null = black (default). Used as fallback when per-level colors are not set.</summary>
    public PdfColor? HeadingColor { get; init; }

    /// <summary>Color for H2 heading text. Null = use HeadingColor.</summary>
    public PdfColor? Heading2Color { get; init; }

    /// <summary>Color for H3 heading text. Null = use HeadingColor.</summary>
    public PdfColor? Heading3Color { get; init; }

    /// <summary>Color for H4 heading text. Null = use HeadingColor.</summary>
    public PdfColor? Heading4Color { get; init; }

    /// <summary>Color for H5 heading text. Null = use HeadingColor.</summary>
    public PdfColor? Heading5Color { get; init; }

    /// <summary>Color for body text. Null = black (default).</summary>
    public PdfColor? BodyColor { get; init; }

    /// <summary>Background color for table header rows. Null = no background (default).</summary>
    public PdfColor? TableHeaderBackground { get; init; }

    /// <summary>Border and grid color for tables. Null = black (default).</summary>
    public PdfColor? TableBorderColor { get; init; }

    /// <summary>Font color for table header text. Null = black (default).</summary>
    public PdfColor? TableHeaderFontColor { get; init; }

    /// <summary>Vertical spacing before/after sections in points. Default: 16 (matches beta.3).</summary>
    public float SectionSpacing { get; init; } = 16f;

    /// <summary>
    /// Distance in points from the top of page 1 to the document title baseline.
    /// Used in place of <see cref="MarginTop"/> + <see cref="TitleMarginTop"/> for
    /// the first page's title only. Default: 36 (matches asciidoctor-pdf's 0.5in
    /// title top offset). Subsequent pages still use the standard MarginTop.
    /// </summary>
    public float TitleFirstPageTop { get; init; } = 36f;

    /// <summary>Vertical spacing above the document title in points. Default: 10.</summary>
    public float TitleMarginTop { get; init; } = 10f;

    /// <summary>Vertical spacing after the document title in points. Default: 16. Set to 0 to remove the gap between title and first section.</summary>
    public float TitleMarginBottom { get; init; } = 16f;

    /// <summary>Indent for nested blocks (admonitions, quotes) in points. Default: 24.</summary>
    public float BlockIndent { get; init; } = 24f;

    // ── Presets ──────────────────────────────────────────────────────────

    /// <summary>Predefined page sizes for convenience.</summary>
    public static PdfRenderOptions Letter => new() { PageWidth = 612f, PageHeight = 792f };

    /// <summary>A4 page size (same as Default).</summary>
    public static PdfRenderOptions A4 => Default;

    /// <summary>Compact preset: smaller fonts, tighter spacing, narrower margins.</summary>
    public static PdfRenderOptions Compact => new()
    {
        FontSize = 10f, LineSpacing = 1.25f,
        ParagraphSpacingAfter = 6f, MarginTop = 54f, MarginBottom = 54f,
        SectionSpacing = 12f
    };

    /// <summary>Presentation preset: larger fonts, wider spacing, heading colors.</summary>
    public static PdfRenderOptions Presentation => new()
    {
        TitleFontSize = 30f, FontSize = 14f, LineSpacing = 1.5f,
        HeadingColor = new PdfColor(0f, 0f, 0.6f), SectionSpacing = 24f
    };
}
