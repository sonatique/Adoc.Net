using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Layout;
using global::Avalonia.Media;
using AdocNet.Layout;

namespace AdocNet.Avalonia;

/// <summary>
/// Renders a <see cref="DocumentLayout"/> into fixed-size pages for a
/// print-like preview. Wraps an <see cref="AvaloniaRenderer"/> (the same
/// pattern as <see cref="IncrementalAvaloniaRenderer"/>): each block is
/// rendered to a control, measured at the page's content width via Avalonia's
/// Measure pass, and blocks are flowed greedily into pages.
///
/// <para>Placement is at <b>block</b> granularity: a block that doesn't fit in
/// the remaining space starts the next page; a block taller than a whole page
/// gets a page of its own and is clipped at the page edge. A
/// <see cref="PageBreakLayout"/> (<c>&lt;&lt;&lt;</c>) forces a new page.</para>
///
/// <para>This is a print-<i>like</i> preview, not a promise of PDF-identical
/// pagination: on-screen text metrics differ from the PDF renderer's font
/// metrics by design. Inline <see cref="AvaloniaRenderer.SourceRangeProperty"/>
/// stamps are preserved, so source mapping keeps working inside pages.</para>
/// </summary>
public sealed class PagedAvaloniaRenderer
{
    private readonly AvaloniaRenderer _renderer;

    public PagedAvaloniaRenderer() : this(new AvaloniaRenderer()) { }

    public PagedAvaloniaRenderer(AvaloniaRenderer renderer)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    /// <summary>
    /// Renders the document into a vertical stack of pages (one
    /// <see cref="Border"/> per page, separated by
    /// <see cref="PageLayoutOptions.PageGap"/>), ready to host in a scrolling
    /// container.
    /// </summary>
    public Control Render(DocumentLayout document, PageLayoutOptions options)
    {
        var pages = RenderPages(document, options);

        var stack = new StackPanel
        {
            Spacing = options.PageGap,
            Margin = new Thickness(options.PageGap),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        foreach (var page in pages)
            stack.Children.Add(page);
        return stack;
    }

    /// <summary>
    /// Renders the document into individual page controls. Always yields at
    /// least one page. Each page is a fixed-size <see cref="Border"/> whose
    /// child stacks the blocks assigned to that page.
    /// </summary>
    public IReadOnlyList<Border> RenderPages(DocumentLayout document, PageLayoutOptions options)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        if (options is null) throw new ArgumentNullException(nameof(options));

        var contentWidth = options.PageWidth - options.PageMargin.Left - options.PageMargin.Right;
        var contentHeight = options.PageHeight - options.PageMargin.Top - options.PageMargin.Bottom;
        if (contentWidth <= 0 || contentHeight <= 0)
            throw new ArgumentException(
                "Page margins leave no room for content.", nameof(options));

        var pages = new List<Border>();
        var current = NewPage(options);
        pages.Add(current);
        var remaining = contentHeight;
        var currentBlocks = (StackPanel)current.Child!;

        void StartNewPage()
        {
            current = NewPage(options);
            pages.Add(current);
            currentBlocks = (StackPanel)current.Child!;
            remaining = contentHeight;
        }

        var measureSize = new Size(contentWidth, double.PositiveInfinity);

        foreach (var control in BlockControls(document))
        {
            if (control is null)
            {
                // A page break: force the boundary (but never emit a trailing
                // blank page for a break at the very start of a fresh page).
                if (currentBlocks.Children.Count > 0)
                    StartNewPage();
                continue;
            }

            control.Measure(measureSize);
            var height = control.DesiredSize.Height;

            if (height > remaining && currentBlocks.Children.Count > 0)
                StartNewPage();

            // An over-tall block sits alone on its page and clips at the edge.
            currentBlocks.Children.Add(control);
            remaining -= height;
        }

        return pages;
    }

    /// <summary>Block controls in document order; null signals a page break.</summary>
    private IEnumerable<Control?> BlockControls(DocumentLayout document)
    {
        if (!string.IsNullOrEmpty(document.Title))
        {
            yield return new TextBlock
            {
                Text = document.Title,
                FontSize = _renderer.Theme.DocumentTitleFontSize,
                FontWeight = FontWeight.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12),
            };
        }

        foreach (var block in document.Children)
        {
            if (block is PageBreakLayout)
            {
                yield return null;
                continue;
            }

            var control = _renderer.Render(block);
            if (control is not null)
                yield return control;
        }
    }

    private static Border NewPage(PageLayoutOptions options) => new()
    {
        Width = options.PageWidth,
        Height = options.PageHeight,
        Background = options.PageBackground,
        BorderBrush = options.PageBorderBrush,
        BorderThickness = new Thickness(options.PageBorderBrush is null ? 0 : options.PageBorderThickness),
        Padding = options.PageMargin,
        ClipToBounds = true,
        Child = new StackPanel(),
    };
}
