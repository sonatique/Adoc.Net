using global::Avalonia;
using global::Avalonia.Controls;
using AdocNet.Ast;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using global::Avalonia.Media.Imaging;
using global::Avalonia.VisualTree;
using AdocNet.Converters.Pdf;
using AdocNet.Layout;

namespace AdocNet.Avalonia;

// Pages that follow a PDF render (PdfRenderer.LayoutPages): same page count, same page
// furniture (title page, TOC, running content, drawn from the PDF's positioned items), and each
// body block starts on the page where the PDF starts it.
public sealed partial class PagedAvaloniaRenderer
{
    private const double DipsPerPoint = 96.0 / 72.0;

    /// <summary>
    /// Renders the document into the pages of a PDF layout, for a print preview that matches the
    /// exported PDF page for page:
    /// <list type="bullet">
    /// <item>one page per PDF page, plus extra pages only if on-screen text runs longer than the PDF's;</item>
    /// <item>the title page, table of contents and header/footer drawn from <paramref name="pdfPages"/>
    /// (text in the PDF's fonts by family name, scaled to the PDF's widths; PNG/JPEG and SVG images);</item>
    /// <item>each body block starting on the page the PDF starts it on. A block that doesn't fit
    /// continues on the next page, cut between text lines.</item>
    /// </list>
    /// Body blocks are this renderer's controls (theme, source ranges, links work as usual). Each page's
    /// <c>Child</c> is a <see cref="Canvas"/>: page items first, then one panel per body
    /// block piece whose <c>Tag</c> is the block's <see cref="SourceRange"/>
    /// (<see cref="SourceRange.None"/> for the document title); a piece continuing a block from the
    /// previous page is shifted up by a <see cref="TranslateTransform"/> and clipped by a parent <see cref="Border"/>.
    /// </summary>
    /// <param name="document">Layout of the same document <paramref name="pdfPages"/> was made from.</param>
    /// <param name="pdfPages">From <c>PdfRenderer.LayoutPages(document, pdfOptions)</c>.</param>
    /// <param name="options">Page chrome and geometry; use <see cref="PageLayoutOptions.FromPdf"/> with the same PDF options.</param>
    public IReadOnlyList<Border> RenderPages(DocumentLayout document, PdfPageLayout pdfPages, PageLayoutOptions options)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (pdfPages is null) throw new ArgumentNullException(nameof(pdfPages));
        if (options is null) throw new ArgumentNullException(nameof(options));

        double contentWidth = options.PageWidth - options.PageMargin.Left - options.PageMargin.Right;
        double top = options.PageMargin.Top, bottom = options.PageHeight - options.PageMargin.Bottom;
        if (contentWidth <= 0 || bottom <= top)
            throw new ArgumentException("Page margins leave no room for content.", nameof(options));

        var pages = new List<Border>();
        var canvases = new List<Canvas>();
        void AddPage()
        {
            var canvas = new Canvas();
            var page = NewPage(options);
            page.Padding = default;
            page.Child = canvas;
            pages.Add(page);
            canvases.Add(canvas);
        }
        foreach (var pdfPage in pdfPages.Pages)
        {
            AddPage();
            foreach (var item in pdfPage.Items)
                if (PageItem(item) is { } control)
                    canvases[^1].Children.Add(control);
        }
        if (pages.Count == 0) AddPage();

        var placements = new Dictionary<SourceRange, PdfBlockPlacement>();
        foreach (var placement in pdfPages.Blocks)
            if (!placements.ContainsKey(placement.Source))
                placements.Add(placement.Source, placement);

        int pageIndex = pdfPages.HasTitlePage && pages.Count > 1 ? 1 : 0;
        double? y = null; // next free y on pageIndex; null = nothing placed there yet

        StackPanel Measured(Control control, SourceRange source)
        {
            var piece = new StackPanel { Width = contentWidth, Tag = source };
            piece.Children.Add(control);
            piece.Measure(new Size(contentWidth, double.PositiveInfinity));
            piece.Arrange(new Rect(0, 0, contentWidth, piece.DesiredSize.Height));
            return piece;
        }

        void Place(StackPanel piece, double offset, double height, bool clip)
        {
            while (pages.Count <= pageIndex) AddPage();
            Control host = piece;
            if (offset > 0) piece.RenderTransform = new TranslateTransform(0, -offset);
            if (clip) host = new Border { Width = contentWidth, Height = height, ClipToBounds = true, Child = piece };
            Canvas.SetLeft(host, options.PageMargin.Left);
            Canvas.SetTop(host, y!.Value);
            canvases[pageIndex].Children.Add(host);
        }

        void Flow(Func<Control?> create, SourceRange source, PdfBlockPlacement? at)
        {
            if (create() is not { } first) return;
            if (at is { } a && a.PageNumber - 1 > pageIndex)
            {
                pageIndex = a.PageNumber - 1;
                y = null;
            }
            var piece = Measured(first, source);
            double height = piece.DesiredSize.Height;
            y ??= at is { } b && b.PageNumber - 1 == pageIndex
                ? Math.Max(0, b.Top * DipsPerPoint - first.Margin.Top)
                : top;

            double offset = 0;
            while (true)
            {
                double avail = bottom - y.Value;
                if (height - offset <= avail)
                {
                    Place(piece, offset, height - offset, clip: offset > 0);
                    y += height - offset;
                    return;
                }
                if (avail > 0)
                {
                    double cut = LineBoundary(piece, offset + avail);
                    if (cut <= offset && y.Value <= top) cut = offset + avail; // not even one line fits a whole page
                    if (cut > offset)
                    {
                        Place(piece, offset, cut - offset, clip: true);
                        offset = cut;
                        piece = Measured(create()!, source); // a control has one parent: each page gets its own copy
                    }
                }
                pageIndex++;
                y = top;
            }
        }

        if (!pdfPages.HasTitlePage && !string.IsNullOrEmpty(document.Title))
            Flow(() => _renderer.CreateTitleBlock(document.Title!), SourceRange.None,
                placements.TryGetValue(SourceRange.None, out var titleAt) ? titleAt : null);

        foreach (var block in document.Children)
        {
            if (block is PageBreakLayout) continue; // the PDF placements already carry page breaks
            if (pdfPages.TocSource is { } toc && block.Source == toc) continue; // drawn from page items
            PdfBlockPlacement? at = !block.Source.IsNone && placements.TryGetValue(block.Source, out var p) ? p : null;
            Flow(() => _renderer.Render(block), block.Source, at);
        }

        return pages;
    }

    /// <summary>
    /// <see cref="RenderPages(DocumentLayout, PdfPageLayout, PageLayoutOptions)"/> as a vertical stack
    /// of pages separated by <see cref="PageLayoutOptions.PageGap"/>, ready for a scrolling container.
    /// </summary>
    public Control Render(DocumentLayout document, PdfPageLayout pdfPages, PageLayoutOptions options)
    {
        var stack = new StackPanel
        {
            Spacing = options?.PageGap ?? 0,
            Margin = new Thickness(options?.PageGap ?? 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        foreach (var page in RenderPages(document, pdfPages, options!))
            stack.Children.Add(page);
        return stack;
    }

    /// <summary>
    /// The largest y ≤ <paramref name="limit"/> (in the arranged piece) that doesn't cut through a
    /// line of text, or 0 when the first line is already below it.
    /// </summary>
    internal static double LineBoundary(Visual piece, double limit)
    {
        var lines = new List<(double Top, double Bottom)>();
        foreach (var textBlock in piece.GetVisualDescendants().OfType<TextBlock>())
        {
            if (textBlock.TranslatePoint(new Point(0, textBlock.Padding.Top), piece) is not { } origin) continue;
            double lineTop = origin.Y;
            foreach (var line in textBlock.TextLayout.TextLines)
            {
                lines.Add((lineTop, lineTop + line.Height));
                lineTop += line.Height;
            }
        }
        double cut = limit;
        for (bool moved = true; moved && cut > 0;)
        {
            moved = false;
            foreach (var (lineTop, lineBottom) in lines)
            {
                if (lineTop < cut - 0.01 && cut < lineBottom - 0.01)
                {
                    cut = lineTop;
                    moved = true;
                }
            }
        }
        return Math.Max(0, cut);
    }

    private Control? PageItem(PdfPageItem item)
    {
        switch (item)
        {
            case PdfPageText text:
            {
                var c = text.Color;
                var block = new TextBlock
                {
                    Text = text.Text,
                    FontSize = text.FontSize * DipsPerPoint,
                    FontWeight = text.Bold ? FontWeight.Bold : FontWeight.Normal,
                    FontStyle = text.Italic ? FontStyle.Italic : FontStyle.Normal,
                    Foreground = new SolidColorBrush(Color.FromRgb(Channel(c.R), Channel(c.G), Channel(c.B))),
                };
                if (text.FontFamily is { } family) block.FontFamily = new FontFamily(family);
                else if (text.Monospace) block.FontFamily = _renderer.Theme.MonospaceFont;
                block.Measure(Size.Infinity);
                // Fit the PDF's advance width, so right-aligned page numbers and dot leaders line up
                // even where the screen font differs from the embedded one.
                double width = block.DesiredSize.Width;
                if (width > 0 && text.Width > 0)
                {
                    double scale = Math.Clamp(text.Width * DipsPerPoint / width, 0.75, 1.33);
                    block.RenderTransformOrigin = RelativePoint.TopLeft;
                    block.RenderTransform = new ScaleTransform(scale, 1);
                }
                var lines = block.TextLayout.TextLines;
                double baseline = lines.Count > 0 ? lines[0].Baseline : block.DesiredSize.Height * 0.8;
                Canvas.SetLeft(block, text.X * DipsPerPoint);
                Canvas.SetTop(block, text.Baseline * DipsPerPoint - baseline);
                return block;
            }
            case PdfPageImage image:
            {
                Control? content = image.Shapes is { } shapes ? Vector(shapes, image.ViewBoxWidth, image.ViewBoxHeight) : Raster(image.Path);
                if (content is null) return null;
                content.Width = image.Width * DipsPerPoint;
                content.Height = image.Height * DipsPerPoint;
                Canvas.SetLeft(content, image.X * DipsPerPoint);
                Canvas.SetTop(content, image.Y * DipsPerPoint);
                return content;
            }
            default:
                return null;
        }
    }

    private static Control? Raster(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            return new Image { Source = new Bitmap(path), Stretch = Stretch.Fill };
        }
        catch (Exception)
        {
            return null; // an undecodable image leaves its area blank, as a broken image does in the PDF
        }
    }

    private static Control? Vector(IReadOnlyList<PdfPageShape> shapes, float viewBoxWidth, float viewBoxHeight)
    {
        if (viewBoxWidth <= 0 || viewBoxHeight <= 0) return null;
        var canvas = new Canvas { Width = viewBoxWidth, Height = viewBoxHeight };
        foreach (var shape in shapes)
        {
            Geometry geometry;
            try { geometry = Geometry.Parse(shape.PathData); }
            catch (Exception) { continue; } // path data the PDF writer also skipped
            var fill = shape.Fill ?? new PdfColor(0, 0, 0);
            canvas.Children.Add(new global::Avalonia.Controls.Shapes.Path
            {
                Data = geometry,
                Fill = new SolidColorBrush(Color.FromRgb(Channel(fill.R), Channel(fill.G), Channel(fill.B))),
            });
        }
        return new Viewbox { Stretch = Stretch.Fill, Child = canvas };
    }

    private static byte Channel(float value) => (byte)Math.Round(Math.Clamp(value, 0f, 1f) * 255);
}
