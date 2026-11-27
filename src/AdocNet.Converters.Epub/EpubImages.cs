namespace AdocNet.Converters.Epub;

/// <summary>
/// The document's local images, read from <see cref="EpubRenderOptions.BaseDirectory"/> and
/// carried in the EPUB, so its chapters show them. An image that is not found, not of a type EPUB
/// readers must show, outside the base directory in safe mode, a URL or a <c>data:</c> URI stays as
/// written.
/// </summary>
internal sealed class EpubImages
{
    /// <summary>An image carried in the EPUB: its archive path, its href from a chapter, its media type.</summary>
    internal sealed record CarriedImage(string ArchivePath, string Href, string MediaType, byte[] Data);

    private readonly string? _baseDirectory;
    private readonly string? _imagesDir;
    private readonly SafeMode _safeMode;
    private readonly Dictionary<string, string?> _hrefs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _taken;

    public EpubImages(string? baseDirectory, string? imagesDir, SafeMode safeMode)
    {
        (_baseDirectory, _imagesDir, _safeMode) = (baseDirectory, imagesDir, safeMode);
        // The book's own files keep their paths: an image never replaces one.
        _taken = new HashSet<string>(
            EpubRenderer.BundledAssets.Select(a => a.ArchivePath.StartsWith("EPUB/", StringComparison.Ordinal) ? a.ArchivePath.Substring(5) : a.ArchivePath)
                .Concat(new[] { "nav.xhtml", "toc.ncx", "package.opf" }),
            StringComparer.OrdinalIgnoreCase);
    }

    public List<CarriedImage> Files { get; } = [];

    /// <summary>The <c>src</c> a chapter gives the image <paramref name="target"/>.</summary>
    public string Source(string target)
    {
        var path = AssetPath.Image(target, _imagesDir);
        if (_baseDirectory is null || AssetPath.IsUri(path))
            return path;
        if (!_hrefs.TryGetValue(path, out var href))
            _hrefs[path] = href = Carry(path);
        return href ?? path;
    }

    private string? Carry(string path)
    {
        var mediaType = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            _ => null, // not a core media type of EPUB 3
        };
        if (mediaType is null)
            return null;

        string file;
        byte[] data;
        try
        {
            var name = Uri.UnescapeDataString(path);
            file = Path.GetFullPath(name.StartsWith("/", StringComparison.Ordinal) ? name : Path.Combine(_baseDirectory!, name));
            if (_safeMode >= SafeMode.Safe && !IsWithin(file, _baseDirectory!))
                return null;
            data = File.ReadAllBytes(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }

        // In the book at its own path when that stays inside the book's folder, else under images/.
        var href = path.Split('/').Contains("..") || path.StartsWith("/", StringComparison.Ordinal)
            ? "images/" + Path.GetFileName(file).Replace(" ", "%20")
            : path.StartsWith("./", StringComparison.Ordinal) ? path.Substring(2) : path;
        for (var n = 2; !_taken.Add(Uri.UnescapeDataString(href)); n++)
            href = $"images/{n}-" + Path.GetFileName(file).Replace(" ", "%20");
        Files.Add(new CarriedImage("EPUB/" + Uri.UnescapeDataString(href), href, mediaType, data));
        return href;
    }

    /// <summary>The base directory itself or a path beneath it, at a separator boundary.</summary>
    private static bool IsWithin(string path, string baseDirectory)
    {
        var root = Path.GetFullPath(baseDirectory);
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase))
            return true;
        var prefix = root.Length > 0 && root[root.Length - 1] == Path.DirectorySeparatorChar ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
