namespace AdocNet.Converters.Epub;

/// <summary>
/// Options controlling EPUB rendering.
/// </summary>
public sealed class EpubRenderOptions : RenderOptions
{
    /// <summary>A shared default instance (no custom CSS).</summary>
    public static new EpubRenderOptions Default { get; } = new();

    /// <summary>
    /// Custom CSS appended to the end of the EPUB's main stylesheet
    /// (<c>styles/epub3.css</c>), so its rules override the bundled ones.
    /// Null = no custom CSS. Fonts it names must be installed on the reader's
    /// device; they are not embedded.
    /// </summary>
    public string? CustomCss { get; init; }
}
