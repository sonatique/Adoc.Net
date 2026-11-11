namespace AdocNet.Converters.Pdf;

/// <summary>
/// Title page layout, mirroring asciidoctor-pdf's <c>title-page</c> theme keys.
/// The title page is drawn when the document sets <c>:title-page:</c> or uses the
/// book doctype. Defaults follow asciidoctor-pdf's default theme.
/// </summary>
public sealed class PdfTitlePageOptions
{
    /// <summary>Alignment of the logo (unless overridden) and of every text block. Default: right.</summary>
    public PdfAlignment Alignment { get; init; } = PdfAlignment.Right;

    /// <summary>Full-page background image (SVG, PNG or JPEG). Null = none.</summary>
    public string? BackgroundImagePath { get; init; }

    /// <summary>Logo image (SVG, PNG or JPEG). Null = none.</summary>
    public string? LogoImagePath { get; init; }

    /// <summary>
    /// Logo top: points below the top of the content area (<c>"130"</c>), a percentage of the
    /// content height (<c>"10%"</c>) or of the page height from the page top (<c>"10vh"</c>).
    /// </summary>
    public string LogoTop { get; init; } = "10%";

    /// <summary>Logo alignment. Null = <see cref="Alignment"/>.</summary>
    public PdfAlignment? LogoAlignment { get; init; }

    /// <summary>Logo width in points. Null = the image's intrinsic width (capped to the content width).</summary>
    public float? LogoWidth { get; init; }

    /// <summary>Main title (the doctitle up to its last ": ").</summary>
    public PdfTitlePageText Title { get; init; } = new() { Top = "55%", LineHeight = 0.9f };

    /// <summary>Subtitle (the doctitle after its last ": ").</summary>
    public PdfTitlePageText Subtitle { get; init; } = new() { Bold = true, Italic = true, LineHeight = 1f };

    /// <summary>Author line(s).</summary>
    public PdfTitlePageText Authors { get; init; } = new();

    /// <summary>Revision line ("Version 1.0, date: remark").</summary>
    public PdfTitlePageText Revision { get; init; } = new();
}

/// <summary>Typography and spacing for one text block on the title page.</summary>
public sealed class PdfTitlePageText
{
    /// <summary>TrueType font file. Null = the body font in the requested weight/style.</summary>
    public string? FontPath { get; init; }

    /// <summary>Font size in points. Null = the body font size.</summary>
    public float? FontSize { get; init; }

    /// <summary>Text color. Null = the body color.</summary>
    public PdfColor? Color { get; init; }

    /// <summary>Use the bold face of the body font (when <see cref="FontPath"/> is null).</summary>
    public bool Bold { get; init; }

    /// <summary>Use the italic face of the body font (when <see cref="FontPath"/> is null).</summary>
    public bool Italic { get; init; }

    /// <summary>Line height as a multiple of the font size. Null = the body line height.</summary>
    public float? LineHeight { get; init; }

    /// <summary>Space above the block, in points.</summary>
    public float MarginTop { get; init; }

    /// <summary>Space below the block, in points.</summary>
    public float MarginBottom { get; init; }

    /// <summary>
    /// Absolute position of the block top (same forms as <see cref="PdfTitlePageOptions.LogoTop"/>).
    /// Only used for the title. Null = flow after the previous block.
    /// </summary>
    public string? Top { get; init; }
}
