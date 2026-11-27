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

    /// <summary>
    /// Folder that relative image paths resolve against, after <c>:imagesdir:</c>. The images found
    /// there are carried in the EPUB and its chapters refer to them. Null: no image is carried; the
    /// chapters refer to the paths as written.
    /// </summary>
    public string? BaseDirectory { get; init; }

    /// <summary>
    /// Safe mode for reading images. At <see cref="SafeMode.Safe"/> or higher (the default), images
    /// are read only from within <see cref="BaseDirectory"/>, so rendering an untrusted document
    /// cannot carry arbitrary local files into the EPUB. Set <see cref="SafeMode.Unsafe"/> to read
    /// images outside it for trusted input.
    /// </summary>
    public SafeMode SafeMode { get; init; } = SafeMode.Safe;
}
