namespace AdocNet.Converters.Html;

/// <summary>
/// Converts image file paths to base64 data URIs for embedding in HTML output.
/// Used when the <c>:data-uri:</c> document attribute is set.
/// </summary>
internal static class DataUriHelper
{
    /// <summary>
    /// Attempts to convert an image path to a data URI string.
    /// Returns null if the file cannot be found or the MIME type is unknown.
    /// </summary>
    /// <param name="imagePath">The image path from the document (may be relative).</param>
    /// <param name="baseDirectory">Base directory for resolving relative paths.</param>
    /// <param name="imagesDir">Optional images directory from <c>:imagesdir:</c> attribute.</param>
    public static string? TryConvertToDataUri(string imagePath, string? baseDirectory, string? imagesDir)
    {
        if (baseDirectory is null)
            return null;

        var mime = GetMimeType(imagePath);
        if (mime is null)
            return null;

        var resolvedPath = ResolvePath(imagePath, baseDirectory, imagesDir);
        if (resolvedPath is null || !File.Exists(resolvedPath))
            return null;

        try
        {
            var bytes = File.ReadAllBytes(resolvedPath);
            var base64 = Convert.ToBase64String(bytes);
            return $"data:{mime};base64,{base64}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the MIME type for an image file based on its extension, or null if unknown.
    /// </summary>
    public static string? GetMimeType(string path)
    {
        var ext = Path.GetExtension(path);
        if (ext.Length == 0) return null;

        return ext.ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".bmp" => "image/bmp",
            _ => null,
        };
    }

    private static string? ResolvePath(string imagePath, string baseDirectory, string? imagesDir)
    {
        if (Path.IsPathRooted(imagePath))
            return Path.GetFullPath(imagePath);

        // If imagesdir is set, resolve relative to baseDirectory/imagesdir
        if (imagesDir is not null && imagesDir.Length > 0)
        {
            var imagesBase = Path.IsPathRooted(imagesDir)
                ? imagesDir
                : Path.Combine(baseDirectory, imagesDir);
            var resolved = Path.GetFullPath(Path.Combine(imagesBase, imagePath));
            if (File.Exists(resolved))
                return resolved;
        }

        // Fall back to baseDirectory
        return Path.GetFullPath(Path.Combine(baseDirectory, imagePath));
    }
}
