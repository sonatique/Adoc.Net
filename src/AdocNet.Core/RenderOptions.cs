namespace AdocNet;

/// <summary>
/// Base class for renderer-specific options.
/// Subclass to add format-specific settings (see <c>HtmlRenderOptions</c>, <c>PdfRenderOptions</c>).
/// </summary>
public class RenderOptions
{
    /// <summary>A shared default instance with no options set.</summary>
    public static RenderOptions Default { get; } = new();
}
