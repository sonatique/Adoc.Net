using AdocNet;

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
    /// <param name="safeMode">
    /// When <see cref="SafeMode.Safe"/> or higher, the resolved image must lie within
    /// <paramref name="baseDirectory"/>; absolute paths and <c>..</c> escapes return null so an
    /// untrusted document cannot embed arbitrary local files via <c>:data-uri:</c>.
    /// </param>
    public static string? TryConvertToDataUri(string imagePath, string? baseDirectory, string? imagesDir, SafeMode safeMode = SafeMode.Safe)
    {
        if (baseDirectory is null)
            return null;

        var mime = GetMimeType(imagePath);
        if (mime is null)
            return null;

        var resolvedPath = ResolvePath(imagePath, baseDirectory, imagesDir);
        if (resolvedPath is null || !File.Exists(resolvedPath))
            return null;

        // Safe mode: refuse to read images outside the document's base directory.
        if (safeMode >= SafeMode.Safe && !IsWithinBaseDirectory(resolvedPath, baseDirectory))
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

    /// <summary>
    /// Returns true when <paramref name="resolvedPath"/> is the base directory itself or a path
    /// strictly beneath it, using a directory-separator boundary so a sibling that merely shares
    /// the base as a string prefix is rejected.
    /// </summary>
    private static bool IsWithinBaseDirectory(string resolvedPath, string baseDirectory)
    {
        var normalizedBase = Path.GetFullPath(baseDirectory);
        if (resolvedPath.Equals(normalizedBase, StringComparison.OrdinalIgnoreCase))
            return true;

        var baseWithSeparator = normalizedBase.Length > 0 && normalizedBase[^1] == Path.DirectorySeparatorChar
            ? normalizedBase
            : normalizedBase + Path.DirectorySeparatorChar;
        return resolvedPath.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase);
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
