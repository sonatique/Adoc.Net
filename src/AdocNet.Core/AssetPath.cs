using System.Text.RegularExpressions;

namespace AdocNet;

/// <summary>
/// Image paths as the converters write them, following Asciidoctor's <c>image_uri</c>: a URL or a
/// <c>data:</c> URI stays as written, its spaces encoded; any other target is joined to
/// <c>:imagesdir:</c> unless it starts with <c>/</c>, its <c>.</c> segments dropped, its <c>..</c>
/// segments folded and its spaces encoded.
/// </summary>
public static class AssetPath
{
    // Asciidoctor's UriSniffRx: a scheme of two or more characters (C: is a drive), a colon, up to two slashes.
    private static readonly Regex UriSniff = new(@"\A\p{L}[\p{L}\p{Nd}.+-]+:/{0,2}", RegexOptions.CultureInvariant);

    /// <summary>The path an image <paramref name="target"/> is written with, given the document's
    /// <c>:imagesdir:</c> (null or empty: none).</summary>
    public static string Image(string target, string? imagesDir) =>
        IsUri(target) ? target.Replace(" ", "%20") : WebPath(target, imagesDir);

    /// <summary>A URL, a <c>data:</c> URI or another URI, as Asciidoctor tells them from paths.</summary>
    public static bool IsUri(string text) => text.IndexOf(':') >= 0 && UriSniff.IsMatch(text);

    /// <summary>Asciidoctor's <c>PathResolver#web_path</c>: <paramref name="target"/> joined to
    /// <paramref name="start"/>, then normalized.</summary>
    private static string WebPath(string target, string? start)
    {
        string? uriPrefix = null;
        if (!string.IsNullOrEmpty(start) && !target.StartsWith("/", StringComparison.Ordinal))
        {
            target = start + (start!.EndsWith("/", StringComparison.Ordinal) ? "" : "/") + target;
            if (target.IndexOf(':') >= 0 && UriSniff.Match(target) is { Success: true } scheme)
            {
                uriPrefix = scheme.Value;
                target = target.Substring(scheme.Length);
            }
        }

        var root = target.StartsWith("/", StringComparison.Ordinal) ? "/"
            : target.StartsWith("./", StringComparison.Ordinal) ? "./"
            : null;
        var segments = (root is null ? target : target.Substring(root.Length)).Split('/').ToList();
        while (segments.Count > 0 && segments[segments.Count - 1].Length == 0)
            segments.RemoveAt(segments.Count - 1); // Ruby's split drops trailing empty fields
        var resolved = new List<string>();
        foreach (var segment in segments)
        {
            if (segment == ".") continue;
            if (segment != "..")
                resolved.Add(segment);
            else if (resolved.Count == 0)
            {
                if (root is null or "./") resolved.Add(segment); // above "/" there is nothing
            }
            else if (resolved[resolved.Count - 1] == "..")
                resolved.Add(segment);
            else
                resolved.RemoveAt(resolved.Count - 1);
        }

        var path = (root ?? "") + string.Join("/", resolved);
        return (uriPrefix ?? "") + path.Replace(" ", "%20");
    }
}
