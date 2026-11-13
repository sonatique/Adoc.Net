using AdocNet.Ast;

namespace AdocNet.Converters.Pdf;

/// <summary>
/// The page structure of a PDF render, from <see cref="PdfRenderer.LayoutPages"/>: page labels,
/// where each top-level block starts, and the items drawn outside the body flow (title page,
/// table of contents, running content). A paged preview uses it to break pages where the PDF does
/// and to draw that page furniture. Lengths are PDF points; the origin is the page's top-left corner.
/// </summary>
public sealed class PdfPageLayout
{
    internal PdfPageLayout(float pageWidth, float pageHeight, IReadOnlyList<PdfLayoutPage> pages,
        IReadOnlyList<PdfBlockPlacement> blocks, bool hasTitlePage, SourceRange? tocSource)
    {
        PageWidth = pageWidth;
        PageHeight = pageHeight;
        Pages = pages;
        Blocks = blocks;
        HasTitlePage = hasTitlePage;
        TocSource = tocSource;
    }

    /// <summary>Page width in points.</summary>
    public float PageWidth { get; }

    /// <summary>Page height in points.</summary>
    public float PageHeight { get; }

    /// <summary>Every page of the PDF, in order.</summary>
    public IReadOnlyList<PdfLayoutPage> Pages { get; }

    /// <summary>
    /// Where each top-level block (a section title, or a block directly in the document or a
    /// section) first draws, in document order. Nested blocks are part of their parent. The
    /// document title, when it is not on a title page, has <see cref="SourceRange.None"/>.
    /// </summary>
    public IReadOnlyList<PdfBlockPlacement> Blocks { get; }

    /// <summary>True when page 1 is a title page (drawn entirely by its <see cref="PdfLayoutPage.Items"/>).</summary>
    public bool HasTitlePage { get; }

    /// <summary>Source of the <c>toc::[]</c> / <c>:toc:</c> block when a TOC was drawn (as page items), else null.</summary>
    public SourceRange? TocSource { get; }
}

/// <summary>One PDF page.</summary>
public sealed class PdfLayoutPage
{
    internal PdfLayoutPage(int number, string label, IReadOnlyList<PdfPageItem> items)
    {
        Number = number;
        Label = label;
        Items = items;
    }

    /// <summary>Physical page number, from 1.</summary>
    public int Number { get; }

    /// <summary>Page label as the PDF viewer shows it: roman before numbering starts (the title page is "i"), then 1, 2…</summary>
    public string Label { get; }

    /// <summary>Title page, table of contents and running content drawn on this page, in paint order.</summary>
    public IReadOnlyList<PdfPageItem> Items { get; }
}

/// <summary>Where a top-level block starts.</summary>
/// <param name="Source">The block's source range (<see cref="SourceRange.None"/> for the document title).</param>
/// <param name="PageNumber">Physical page (from 1) of the block's first mark.</param>
/// <param name="Top">Distance in points from the page top to the top of the block's first mark.</param>
public readonly record struct PdfBlockPlacement(SourceRange Source, int PageNumber, float Top);

/// <summary>An item drawn on a page outside the body flow.</summary>
public abstract class PdfPageItem
{
    private protected PdfPageItem() { }
}

/// <summary>A run of text on one baseline.</summary>
public sealed class PdfPageText : PdfPageItem
{
    internal PdfPageText(string text, float x, float baseline, float width, float fontSize,
        string? fontFamily, bool bold, bool italic, bool monospace, PdfColor color)
    {
        Text = text; X = x; Baseline = baseline; Width = width; FontSize = fontSize;
        FontFamily = fontFamily; Bold = bold; Italic = italic; Monospace = monospace; Color = color;
    }

    public string Text { get; }
    /// <summary>Left edge, points from the page's left edge.</summary>
    public float X { get; }
    /// <summary>Baseline, points from the page top.</summary>
    public float Baseline { get; }
    /// <summary>Advance width in the PDF (points), to fit other fonts to the same extent.</summary>
    public float Width { get; }
    public float FontSize { get; }
    /// <summary>Font family name of the embedded font, or null for the PDF's built-in Helvetica/Courier.</summary>
    public string? FontFamily { get; }
    public bool Bold { get; }
    public bool Italic { get; }
    /// <summary>True for the built-in Courier faces.</summary>
    public bool Monospace { get; }
    public PdfColor Color { get; }
}

/// <summary>An image: a raster file (<see cref="Path"/>) or vector shapes parsed from an SVG.</summary>
public sealed class PdfPageImage : PdfPageItem
{
    internal PdfPageImage(string? path, IReadOnlyList<PdfPageShape>? shapes, float viewBoxWidth, float viewBoxHeight,
        float x, float y, float width, float height)
    {
        Path = path; Shapes = shapes; ViewBoxWidth = viewBoxWidth; ViewBoxHeight = viewBoxHeight;
        X = x; Y = y; Width = width; Height = height;
    }

    /// <summary>PNG or JPEG file; null for an SVG (see <see cref="Shapes"/>).</summary>
    public string? Path { get; }
    /// <summary>Filled SVG paths in viewBox units; null for a raster image.</summary>
    public IReadOnlyList<PdfPageShape>? Shapes { get; }
    public float ViewBoxWidth { get; }
    public float ViewBoxHeight { get; }
    public float X { get; }
    /// <summary>Top edge, points from the page top.</summary>
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
}

/// <summary>A filled SVG path (SVG path data, viewBox units). A null fill is black.</summary>
public readonly record struct PdfPageShape(PdfColor? Fill, string PathData);

/// <summary>Collects a <see cref="PdfPageLayout"/> while a render runs (one per pass).</summary>
internal sealed class PdfPageRecorder
{
    private readonly List<List<PdfPageItem>> _items = [];
    internal readonly List<PdfBlockPlacement> Blocks = [];
    private SourceRange? _pending;

    /// <summary>True while drawing page furniture (title page, TOC, running content).</summary>
    internal bool Capturing { get; set; }
    internal SourceRange? TocSource { get; set; }
    internal bool HasTitlePage { get; set; }

    /// <summary>A top-level block starts: its first mark (see <see cref="Ink"/>) places it.</summary>
    internal void BeginBlock(SourceRange source) => _pending = source;

    /// <summary>Body blocks are over: a block that drew nothing must not take later marks (footnotes, running content).</summary>
    internal void EndBlocks() => _pending = null;

    internal void Ink(int pageNumber, float pageHeight, float topY)
    {
        if (_pending is not { } source) return;
        _pending = null;
        Blocks.Add(new PdfBlockPlacement(source, pageNumber, pageHeight - topY));
    }

    internal void Add(int pageNumber, PdfPageItem item)
    {
        while (_items.Count < pageNumber) _items.Add([]);
        _items[pageNumber - 1].Add(item);
    }

    internal PdfPageLayout Build(float pageWidth, float pageHeight, int pageCount, int numberingOffset)
    {
        var pages = new List<PdfLayoutPage>(pageCount);
        for (int i = 1; i <= pageCount; i++)
        {
            var label = i <= numberingOffset && numberingOffset < pageCount
                ? Roman(i)
                : (i - (numberingOffset < pageCount ? numberingOffset : 0)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            pages.Add(new PdfLayoutPage(i, label, i <= _items.Count ? _items[i - 1] : []));
        }
        return new PdfPageLayout(pageWidth, pageHeight, pages, Blocks, HasTitlePage, TocSource);
    }

    private static string Roman(int n)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (value, digits) in new[] { (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"),
                     (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i") })
            for (; n >= value; n -= value) sb.Append(digits);
        return sb.ToString();
    }
}
