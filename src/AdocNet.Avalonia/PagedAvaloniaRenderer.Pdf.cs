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

        void Flow(Func<Control?> create, SourceRange source, PdfBlockPlacement? at, BlockLayout? block = null)
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

            var size = piece.DesiredSize;
            PieceGeometry? geometry = null; // read before the piece is placed, while it is laid out alone
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
                    geometry ??= new PieceGeometry(piece);
                    double cut = LineBoundary(geometry.Lines, offset + avail);
                    if (cut <= offset && y.Value <= top) cut = offset + avail; // not even one line fits a whole page
                    if (cut > offset)
                    {
                        Place(piece, offset, cut - offset, clip: true);
                        offset = cut;
                        // A control has one parent: each page gets its own copy, laid out only where the page shows it.
                        var copy = (block is null ? null : RenderPart(block, first, geometry, offset, offset + (bottom - top))) ?? create()!;
                        Trim(first, copy, geometry, offset, offset + (bottom - top));
                        piece = Measured(copy, source);
                        if (piece.DesiredSize != size) piece = Measured(create()!, source);
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
            Flow(() => _renderer.Render(block), block.Source, at, block);
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
    /// Positions in a block piece laid out alone, read once and used for every page the block
    /// continues on.
    /// </summary>
    private sealed class PieceGeometry
    {
        /// <summary>Text lines, top and bottom, in the piece.</summary>
        internal readonly List<(double Top, double Bottom)> Lines = [];

        /// <summary>Bounds of each descendant, in the piece.</summary>
        internal readonly Dictionary<Control, Rect> Bounds = [];

        /// <summary>Top of the first text line in the piece and the height of each line, per text block.</summary>
        internal readonly Dictionary<TextBlock, (double Top, double[] Heights)> Text = [];

        /// <summary>Height of each row, per grid.</summary>
        internal readonly Dictionary<Grid, double[]> RowHeights = [];

        private readonly Dictionary<TextBlock, string[]> _textLines = [];

        /// <summary>The text of <paramref name="textBlock"/> split at line feeds, split once.</summary>
        internal string[] TextLines(TextBlock textBlock, string text)
        {
            if (!_textLines.TryGetValue(textBlock, out var lines))
                _textLines[textBlock] = lines = text.Split('\n');
            return lines;
        }

        internal PieceGeometry(Visual piece)
        {
            foreach (var control in piece.GetVisualDescendants().OfType<Control>())
            {
                if (control.TranslatePoint(default, piece) is { } at)
                    Bounds[control] = new Rect(at, control.Bounds.Size);
                if (control is Grid grid)
                    RowHeights[grid] = grid.RowDefinitions.Select(r => r.ActualHeight).ToArray();
                if (control is not TextBlock textBlock
                    || textBlock.TranslatePoint(new Point(0, textBlock.Padding.Top), piece) is not { } origin) continue;
                double lineTop = origin.Y;
                var textLines = textBlock.TextLayout.TextLines;
                var heights = new double[textLines.Count];
                for (int i = 0; i < heights.Length; i++)
                {
                    heights[i] = textLines[i].Height;
                    Lines.Add((lineTop, lineTop + heights[i]));
                    lineTop += heights[i];
                }
                Text[textBlock] = (origin.Y, heights);
            }
        }
    }

    /// <summary>
    /// A copy of the list or table <paramref name="laidOut"/> was rendered from, with only the items or
    /// rows reaching into the band from <paramref name="top"/> to <paramref name="bottom"/> of the piece
    /// built (and the rows that a cell in the band spans), the others replaced by empty space of the same
    /// height. Null for other blocks, or when the renderer renders blocks its own way.
    /// </summary>
    private Control? RenderPart(BlockLayout block, Control laidOut, PieceGeometry geometry, double top, double bottom)
    {
        if (!_renderer.RendersBlocksItself) return null;
        bool InBand(Control control) =>
            geometry.Bounds.TryGetValue(control, out var bounds) && bounds.Bottom > top && bounds.Top < bottom;

        switch (block)
        {
            case ListLayout list when laidOut is StackPanel panel && panel.Children.Count == list.Items.Count:
            {
                int first = -1, last = -1;
                for (int i = 0; i < panel.Children.Count; i++)
                {
                    if (!InBand(panel.Children[i])) continue;
                    if (first < 0) first = i;
                    last = i;
                }
                if (first < 0) return null;
                double before = 0, after = 0;
                for (int i = 0; i < first; i++) before += panel.Children[i].DesiredSize.Height;
                for (int i = last + 1; i < panel.Children.Count; i++) after += panel.Children[i].DesiredSize.Height;
                return _renderer.RenderListPart(list, first, last, before, after);
            }
            case TableLayout table when laidOut is StackPanel { Children: [.., Border { Child: Grid grid }] }
                                        && geometry.RowHeights.TryGetValue(grid, out var heights):
            {
                int first = int.MaxValue, last = -1;
                foreach (var cell in grid.Children)
                {
                    if (!InBand(cell)) continue;
                    first = Math.Min(first, Grid.GetRow(cell));
                    last = Math.Max(last, Grid.GetRow(cell) + Math.Max(1, Grid.GetRowSpan(cell)) - 1);
                }
                if (last < 0) return null;
                var columns = grid.ColumnDefinitions.Select(c => (c.Width, c.MinWidth)).ToArray();
                return _renderer.RenderTablePart(table, new AvaloniaRenderer.TablePart(columns, heights, first, Math.Min(last, heights.Length - 1)));
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// Makes <paramref name="copy"/>, a new instance of the control <paramref name="laidOut"/> was made from,
    /// cheap to lay out when only the band from <paramref name="top"/> to <paramref name="bottom"/> of the
    /// piece is shown: children wholly outside the band become empty controls of the same size, and an
    /// unwrapped multi-line text keeps only its lines in the band. The band shows the same content.
    /// </summary>
    private static void Trim(Control laidOut, Control copy, PieceGeometry geometry, double top, double bottom)
    {
        if (laidOut.GetType() != copy.GetType()) return;
        switch (laidOut)
        {
            case Panel panel when copy is Panel copyPanel && copyPanel.Children.Count == panel.Children.Count:
                for (int i = 0; i < panel.Children.Count; i++)
                {
                    var child = panel.Children[i];
                    if (!geometry.Bounds.TryGetValue(child, out var bounds)) continue;
                    if (bounds.Bottom <= top || bounds.Top >= bottom)
                        copyPanel.Children[i] = Placeholder(child, copyPanel.Children[i]);
                    else
                        Trim(child, copyPanel.Children[i], geometry, top, bottom);
                }
                break;
            case Decorator { Child: { } child } when copy is Decorator { Child: { } copyChild }:
                Trim(child, copyChild, geometry, top, bottom);
                break;
            case TextBlock text when copy is TextBlock copyText:
                KeepLinesInBand(text, copyText, geometry, top, bottom);
                break;
        }
    }

    /// <summary>An empty control taking the room <paramref name="laidOut"/> takes in its parent.</summary>
    private static Control Placeholder(Control laidOut, Control copy)
    {
        var margin = laidOut.Margin;
        var placeholder = new Border
        {
            Width = Math.Max(0, laidOut.DesiredSize.Width - margin.Left - margin.Right),
            Height = Math.Max(0, laidOut.DesiredSize.Height - margin.Top - margin.Bottom),
            Margin = margin,
            HorizontalAlignment = laidOut.HorizontalAlignment,
            VerticalAlignment = laidOut.VerticalAlignment,
        };
        Grid.SetRow(placeholder, Grid.GetRow(copy));
        Grid.SetColumn(placeholder, Grid.GetColumn(copy));
        Grid.SetRowSpan(placeholder, Grid.GetRowSpan(copy));
        Grid.SetColumnSpan(placeholder, Grid.GetColumnSpan(copy));
        return placeholder;
    }

    /// <summary>
    /// Keeps in <paramref name="copy"/> only the lines of an unwrapped text that reach into the band: the
    /// height of the lines above becomes top padding, and the copy keeps the full text's height.
    /// </summary>
    private static void KeepLinesInBand(TextBlock laidOut, TextBlock copy, PieceGeometry geometry, double top, double bottom)
    {
        if (laidOut.TextWrapping != TextWrapping.NoWrap || laidOut.Text is not { } text || copy.Text != text
            || laidOut.Inlines is { Count: > 0 } || !geometry.Text.TryGetValue(laidOut, out var layout)
            || !geometry.Bounds.TryGetValue(laidOut, out var bounds)) return;
        var lines = geometry.TextLines(laidOut, text);
        var heights = layout.Heights;
        if (lines.Length != heights.Length) return;

        int first = 0;
        double lineTop = layout.Top, above = 0;
        while (first < lines.Length && lineTop + heights[first] <= top)
        {
            above += heights[first];
            lineTop += heights[first];
            first++;
        }
        int last = first - 1;
        while (last + 1 < lines.Length && lineTop < bottom)
        {
            last++;
            lineTop += heights[last];
        }
        if (last < first || first == 0 && last == lines.Length - 1) return;

        copy.Text = string.Join("\n", lines, first, last - first + 1);
        var padding = laidOut.Padding;
        copy.Padding = new Thickness(padding.Left, padding.Top + above, padding.Right, padding.Bottom);
        copy.Height = bounds.Height;
    }

    /// <summary>
    /// The largest y ≤ <paramref name="limit"/> (in the arranged piece) that doesn't cut through one of
    /// its text <paramref name="lines"/>, or 0 when the first line is already below it.
    /// </summary>
    private static double LineBoundary(List<(double Top, double Bottom)> lines, double limit)
    {
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
