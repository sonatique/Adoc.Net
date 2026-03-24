using AdocNet;

namespace AdocNet.Converters.Pdf;

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

    /// <summary>Enable hyphenation in body text. Default: false.</summary>
    public bool EnableHyphenation { get; init; }

    /// <summary>Spacing before paragraphs in points. Default: 0.</summary>
    public float ParagraphSpacingBefore { get; init; } = 0f;

    /// <summary>Spacing after paragraphs in points. Default: 8 (matches beta.3).</summary>
    public float ParagraphSpacingAfter { get; init; } = 8f;

    // ── Headers and footers ──────────────────────────────────────────────

    /// <summary>Show page numbers in footer. Default: false.</summary>
    public bool ShowPageNumbers { get; init; }

    /// <summary>Header text. Null = no header. Supports {page} and {pages} placeholders.</summary>
    public string? HeaderText { get; init; }

    /// <summary>Footer text. Null = no footer (unless ShowPageNumbers). Supports {page} and {pages}.</summary>
    public string? FooterText { get; init; }

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

    /// <summary>Left border width for admonition blocks in points. Default: 2.</summary>
    public float AdmonitionBorderWidth { get; init; } = 2f;

    /// <summary>Repeat header row when a table spans pages. Default: true.</summary>
    public bool RepeatTableHeader { get; init; } = true;

    /// <summary>Color for heading text (h1–h5). Null = black (default).</summary>
    public PdfColor? HeadingColor { get; init; }

    /// <summary>Color for body text. Null = black (default).</summary>
    public PdfColor? BodyColor { get; init; }

    /// <summary>Background color for table header rows. Null = no background (default).</summary>
    public PdfColor? TableHeaderBackground { get; init; }

    /// <summary>Vertical spacing before/after sections in points. Default: 16 (matches beta.3).</summary>
    public float SectionSpacing { get; init; } = 16f;

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
