namespace AdocNet;

/// <summary>
/// Base class for renderer-specific options.
/// Subclass to add format-specific settings (see <c>HtmlRenderOptions</c>, <c>PdfRenderOptions</c>).
/// </summary>
public class RenderOptions
{
    /// <summary>A shared default instance with no options set.</summary>
    public static RenderOptions Default { get; } = new();

    /// <summary>
    /// Draws STEM content as outlines (HTML and EPUB: inline SVG, no MathJax script; PDF: vector
    /// paths). Null (the default) keeps each converter's own handling: MathJax delimiters in HTML,
    /// the source text elsewhere. DocBook always writes its own equation markup.
    /// </summary>
    public IStemRenderer? StemRenderer { get; init; }
}
