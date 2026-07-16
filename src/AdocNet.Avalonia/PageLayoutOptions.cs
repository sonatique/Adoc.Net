using global::Avalonia;
using global::Avalonia.Media;

namespace AdocNet.Avalonia;

/// <summary>
/// Page geometry and paper chrome for <see cref="PagedAvaloniaRenderer"/>.
/// All lengths are in Avalonia device-independent pixels (96/inch). Use
/// <see cref="FromPdfPoints"/> to mirror a PDF option set expressed in
/// PostScript points (72/inch), so the preview's page geometry matches the
/// exported document exactly.
/// </summary>
public sealed class PageLayoutOptions
{
    private const double DipsPerPoint = 96.0 / 72.0;

    /// <summary>Page width in DIPs. Default: A4 (794 ≈ 210 mm).</summary>
    public double PageWidth { get; init; } = 794;

    /// <summary>Page height in DIPs. Default: A4 (1123 ≈ 297 mm).</summary>
    public double PageHeight { get; init; } = 1123;

    /// <summary>Content margins inside each page. Default: 96 (one inch),
    /// matching the PDF renderer's default 72 pt margins.</summary>
    public Thickness PageMargin { get; init; } = new(96);

    /// <summary>Vertical gap between pages in the stacked view.</summary>
    public double PageGap { get; init; } = 24;

    /// <summary>Paper fill.</summary>
    public IBrush PageBackground { get; init; } = Brushes.White;

    /// <summary>Paper edge; null for borderless pages.</summary>
    public IBrush? PageBorderBrush { get; init; } = new SolidColorBrush(Color.FromRgb(0xD0, 0xD0, 0xD0));

    /// <summary>Paper edge thickness (ignored when <see cref="PageBorderBrush"/> is null).</summary>
    public double PageBorderThickness { get; init; } = 1;

    /// <summary>A4 portrait with one-inch margins (the default).</summary>
    public static PageLayoutOptions A4 => new();

    /// <summary>US Letter portrait with one-inch margins.</summary>
    public static PageLayoutOptions Letter => new() { PageWidth = 816, PageHeight = 1056 };

    /// <summary>
    /// Build options from a page geometry expressed in PostScript points
    /// (72/inch) — the unit of <c>PdfRenderOptions</c> — so a paged preview
    /// can mirror the PDF export's page setup.
    /// </summary>
    public static PageLayoutOptions FromPdfPoints(
        double pageWidth, double pageHeight,
        double marginLeft = 72, double marginTop = 72,
        double marginRight = 72, double marginBottom = 72)
    {
        return new PageLayoutOptions
        {
            PageWidth = pageWidth * DipsPerPoint,
            PageHeight = pageHeight * DipsPerPoint,
            PageMargin = new Thickness(
                marginLeft * DipsPerPoint,
                marginTop * DipsPerPoint,
                marginRight * DipsPerPoint,
                marginBottom * DipsPerPoint),
        };
    }
}
