using global::Avalonia.Media;

namespace AdocNet.Avalonia;

/// <summary>
/// Visual styling for <see cref="AvaloniaRenderer"/>: brushes, fonts and sizes.
/// Construct one, tweak the properties (e.g. for a dark theme or to match the
/// host application's palette), and assign it to
/// <see cref="AvaloniaRenderer.Theme"/>. Every renderer starts with its own
/// copy of the defaults, so mutating a renderer's theme never affects others.
/// </summary>
public sealed class AvaloniaRenderTheme
{
    /// <summary>Font family for monospace (code / literal) content.</summary>
    public FontFamily MonospaceFont { get; set; } = new("Cascadia Mono, Consolas, Courier New, monospace");

    /// <summary>Foreground brush for hyperlinks.</summary>
    public IBrush LinkBrush { get; set; } = new SolidColorBrush(Color.FromRgb(0, 102, 204));

    /// <summary>Background brush for code/listing blocks.</summary>
    public IBrush CodeBlockBackground { get; set; } = new SolidColorBrush(Color.FromRgb(245, 245, 245));

    /// <summary>Foreground brush for the language label above a source block.</summary>
    public IBrush CodeLanguageForeground { get; set; } = new SolidColorBrush(Color.FromRgb(136, 136, 136));

    /// <summary>Brush for table cell borders.</summary>
    public IBrush TableBorderBrush { get; set; } = new SolidColorBrush(Color.FromRgb(200, 200, 200));

    /// <summary>Background brush for table header cells.</summary>
    public IBrush TableHeaderBackground { get; set; } = new SolidColorBrush(Color.FromRgb(240, 240, 240));

    /// <summary>Brush for thematic-break (horizontal rule) lines.</summary>
    public IBrush ThematicBreakBrush { get; set; } = new SolidColorBrush(Color.FromRgb(200, 200, 200));

    /// <summary>Foreground brush for description-list terms.</summary>
    public IBrush DescriptionTermForeground { get; set; } = new SolidColorBrush(Color.FromRgb(60, 60, 60));

    /// <summary>Font size for the document title (the level-0 <c>= Title</c>).</summary>
    public double DocumentTitleFontSize { get; set; } = 28;

    /// <summary>
    /// Font sizes for heading levels 1..N (index 0 = level 1). Levels beyond
    /// the list fall back to <see cref="FallbackHeadingFontSize"/>.
    /// </summary>
    public IReadOnlyList<double> HeadingFontSizes { get; set; } = new double[] { 24, 20, 18, 16, 14 };

    /// <summary>Font size for heading levels beyond <see cref="HeadingFontSizes"/>.</summary>
    public double FallbackHeadingFontSize { get; set; } = 13;

    /// <summary>Returns the font size for the given 1-based heading level.</summary>
    public double HeadingFontSize(int level) =>
        level >= 1 && level <= HeadingFontSizes.Count ? HeadingFontSizes[level - 1] : FallbackHeadingFontSize;

    /// <summary>
    /// Reference body font size used to size superscript/subscript runs (including
    /// footnote markers). Body text itself inherits its size from the host container
    /// (so the host stays in control); set this to match when the host uses a
    /// non-default body size, so super/subscript stay proportionally smaller.
    /// </summary>
    public double BodyFontSize { get; set; } = 14;

    /// <summary>
    /// Fraction of <see cref="BodyFontSize"/> used for superscript/subscript glyphs.
    /// </summary>
    public double SubSuperscriptFontScale { get; set; } = 0.7;

    /// <summary>Font size for superscript/subscript runs.</summary>
    public double SubSuperscriptFontSize => BodyFontSize * SubSuperscriptFontScale;
}

/// <summary>
/// Event data for <see cref="AvaloniaRenderer.LinkClicked"/>. Set
/// <see cref="Handled"/> to suppress the renderer's default behaviour of
/// opening the URL with the OS shell — e.g. to route <c>xref:</c> targets
/// internally or to sandbox external navigation.
/// </summary>
public sealed class LinkClickedEventArgs : EventArgs
{
    public LinkClickedEventArgs(string url) => Url = url;

    /// <summary>The link target (href) that was clicked.</summary>
    public string Url { get; }

    /// <summary>When set to true, the renderer does not open the URL itself.</summary>
    public bool Handled { get; set; }
}
