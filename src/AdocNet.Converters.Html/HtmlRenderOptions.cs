using AdocNet;

namespace AdocNet.Converters.Html;

/// <summary>
/// Options controlling HTML rendering: theming, full-document wrapping, and custom CSS.
/// </summary>
public sealed class HtmlRenderOptions : RenderOptions
{
    /// <summary>A shared default instance (no theme, fragment output).</summary>
    public static new HtmlRenderOptions Default { get; } = new();

    /// <summary>
    /// The built-in theme to apply. Default: <see cref="HtmlTheme.None"/> (bare fragment).
    /// When set to a theme other than None, the output is wrapped in a full HTML document
    /// with the theme CSS embedded in a &lt;style&gt; block.
    /// </summary>
    public HtmlTheme Theme { get; init; } = HtmlTheme.None;

    /// <summary>
    /// Custom CSS to append after the theme CSS. Null = no custom CSS.
    /// Only effective when <see cref="Theme"/> is not <see cref="HtmlTheme.None"/>.
    /// </summary>
    public string? CustomCss { get; init; }

    /// <summary>
    /// When true, wraps the HTML content in a full document (&lt;!DOCTYPE html&gt;...&lt;/html&gt;)
    /// even if <see cref="Theme"/> is <see cref="HtmlTheme.None"/>.
    /// When <see cref="Theme"/> is set to a value other than None, this is implicitly true.
    /// Default: false.
    /// </summary>
    public bool FullDocument { get; init; }

    /// <summary>
    /// The &lt;title&gt; element for full-document mode. Null = use document title or omit.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// Additional &lt;head&gt; content (e.g., &lt;link&gt; or &lt;meta&gt; tags).
    /// Inserted verbatim into the &lt;head&gt; section. Null = none.
    /// Only effective in full-document mode.
    /// </summary>
    public string? ExtraHead { get; init; }

    /// <summary>
    /// When true and a supported language is specified, source blocks are highlighted
    /// server-side using the built-in tokenizer. When false, source blocks are emitted
    /// as plain text (for client-side highlighting). Default: true.
    /// Ignored when :source-highlighter: highlight.js is set (always defers to client).
    /// </summary>
    public bool EnableSyntaxHighlighting { get; init; }

    /// <summary>Whether the output should be a full HTML document.</summary>
    internal bool IsFullDocument => FullDocument || Theme != HtmlTheme.None;

    /// <summary>
    /// When true, wraps each top-level block in HTML comment markers
    /// (<c>&lt;!-- sect:N --&gt;</c> / <c>&lt;!-- /sect:N --&gt;</c>) to enable
    /// incremental rendering. The markers are invisible and do not affect visual output.
    /// Default: false.
    /// </summary>
    public bool EnableIncrementalMarkers { get; init; }

    /// <summary>
    /// Base directory for resolving relative image paths when <c>:data-uri:</c> is set.
    /// Also used for docinfo file lookup. When null, data-uri falls back to literal paths.
    /// </summary>
    public string? BaseDirectory { get; init; }

    /// <summary>Convenience: full document with Default theme.</summary>
    public static HtmlRenderOptions Styled => new() { Theme = HtmlTheme.Default };
}
