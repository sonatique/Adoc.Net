using AdocNet;

namespace AdocNet.Converters.Pdf;

/// <summary>
/// Options controlling PDF rendering: page size, margins, headers, footers, and page numbers.
/// </summary>
public sealed class PdfRenderOptions : RenderOptions
{
    /// <summary>A shared default instance (A4 page, 1-inch margins, no header/footer).</summary>
    public static new PdfRenderOptions Default { get; } = new();

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

    /// <summary>Show page numbers in footer. Default: false.</summary>
    public bool ShowPageNumbers { get; init; }

    /// <summary>Header text. Null = no header. Supports {page} placeholder.</summary>
    public string? HeaderText { get; init; }

    /// <summary>Footer text. Null = no footer (unless ShowPageNumbers). Supports {page} placeholder.</summary>
    public string? FooterText { get; init; }

    /// <summary>Base directory for resolving relative image paths. Null = images fall back to placeholder.</summary>
    public string? BaseDirectory { get; init; }

    /// <summary>Path to a TrueType font file (.ttf) for body text. Null = use standard Helvetica.</summary>
    public string? FontPath { get; init; }

    /// <summary>Path to a TrueType font file for bold text. Null = use FontPath or standard Helvetica-Bold.</summary>
    public string? BoldFontPath { get; init; }

    /// <summary>Path to a TrueType font file for italic text. Null = use standard Helvetica-Oblique.</summary>
    public string? ItalicFontPath { get; init; }

    /// <summary>Path to a TrueType font file for monospace text. Null = use standard Courier.</summary>
    public string? MonoFontPath { get; init; }

    /// <summary>Predefined page sizes for convenience.</summary>
    public static PdfRenderOptions Letter => new() { PageWidth = 612f, PageHeight = 792f };

    /// <summary>A4 page size (same as Default).</summary>
    public static PdfRenderOptions A4 => Default;
}
