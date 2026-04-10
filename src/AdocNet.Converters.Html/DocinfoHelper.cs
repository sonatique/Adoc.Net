namespace AdocNet.Converters.Html;

/// <summary>
/// Reads docinfo files (header and footer) for injection into HTML output.
/// Controlled by the <c>:docinfo:</c> document attribute.
/// </summary>
internal static class DocinfoHelper
{
    /// <summary>
    /// Reads header docinfo content to inject at the end of <c>&lt;head&gt;</c>.
    /// Returns null if no docinfo is configured or no files are found.
    /// </summary>
    public static string? ReadHeaderDocinfo(
        IReadOnlyDictionary<string, string> attributes, string? baseDirectory)
    {
        if (baseDirectory is null) return null;
        if (!attributes.TryGetValue("docinfo", out var mode)) return null;

        var docname = GetDocname(attributes);
        var parts = new List<string>();

        if (IncludesSharedHead(mode))
            TryAppendFile(parts, Path.Combine(baseDirectory, "docinfo.html"));

        if (IncludesPrivateHead(mode) && docname is not null)
            TryAppendFile(parts, Path.Combine(baseDirectory, $"{docname}-docinfo.html"));

        return parts.Count > 0 ? string.Join("\n", parts) : null;
    }

    /// <summary>
    /// Reads footer docinfo content to inject before <c>&lt;/body&gt;</c>.
    /// Returns null if no docinfo is configured or no files are found.
    /// </summary>
    public static string? ReadFooterDocinfo(
        IReadOnlyDictionary<string, string> attributes, string? baseDirectory)
    {
        if (baseDirectory is null) return null;
        if (!attributes.TryGetValue("docinfo", out var mode)) return null;

        var docname = GetDocname(attributes);
        var parts = new List<string>();

        if (IncludesSharedFooter(mode))
            TryAppendFile(parts, Path.Combine(baseDirectory, "docinfo-footer.html"));

        if (IncludesPrivateFooter(mode) && docname is not null)
            TryAppendFile(parts, Path.Combine(baseDirectory, $"{docname}-docinfo-footer.html"));

        return parts.Count > 0 ? string.Join("\n", parts) : null;
    }

    private static string? GetDocname(IReadOnlyDictionary<string, string> attributes)
    {
        if (attributes.TryGetValue("docname", out var name) && name.Length > 0)
            return name;
        return null;
    }

    private static bool IncludesSharedHead(string mode)
    {
        return mode is "" or "shared" or "shared-head"
            || mode.Contains("shared", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IncludesSharedFooter(string mode)
    {
        return mode is "" or "shared" or "shared-footer"
            || mode.Contains("shared", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IncludesPrivateHead(string mode)
    {
        return mode is "private" or "private-head"
            || mode.Contains("private", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IncludesPrivateFooter(string mode)
    {
        return mode is "private" or "private-footer"
            || mode.Contains("private", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryAppendFile(List<string> parts, string path)
    {
        try
        {
            if (File.Exists(path))
                parts.Add(File.ReadAllText(path).TrimEnd());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Silently skip unreadable docinfo files
        }
    }
}
