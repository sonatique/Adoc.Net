using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AdocNet.Importers.Docx;

namespace AdocNet.Importers;

/// <summary>
/// Collects the document header (title, attributes), body blocks and extracted media, and writes
/// the AsciiDoc source. Shared by the HTML (and so Markdown) and PDF importers.
/// </summary>
internal sealed class AsciiDocBuilder
{
    private readonly List<KeyValuePair<string, string>> _attributes = [];
    private readonly List<ImportMediaItem> _media = [];
    private readonly HashSet<string> _mediaPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly ImportOptions _options;

    public AsciiDocBuilder(ImportOptions options) => _options = options;

    public ImportReport Report { get; } = new();
    public string? Title { get; set; }
    public IReadOnlyList<ImportMediaItem> Media => _media;

    /// <summary>Sets a header attribute (first value wins; the name is normalized to AsciiDoc's rules).</summary>
    public void SetAttribute(string name, string value)
    {
        var key = Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9_-]+", "-").Trim('-');
        if (key.Length == 0 || !char.IsLetterOrDigit(key[0]) && key[0] != '_') return;
        if (_attributes.Any(a => a.Key == key)) return;
        _attributes.Add(new(key, value.Replace("\r", "").Replace("\n", " ").Trim()));
    }

    /// <summary>Stores image bytes as a media file and returns its relative path.</summary>
    public string AddMedia(string source, byte[] content, string extension)
    {
        string path;
        int n = _media.Count + 1;
        do path = $"{_options.MediaDirectoryName}/image{n++.ToString(CultureInfo.InvariantCulture)}.{extension}";
        while (!_mediaPaths.Add(path));
        _media.Add(new ImportMediaItem { Source = source, RelativePath = path, Content = content });
        return path;
    }

    /// <summary>The document: header, then the blocks separated by blank lines.</summary>
    public string Build(IEnumerable<string> blocks)
    {
        var sb = new StringBuilder();
        if (Title is { Length: > 0 } title)
            sb.Append("= ").Append(Inline(title)).Append('\n');
        foreach (var (key, value) in _attributes)
            sb.Append(':').Append(key).Append(": ").Append(value).Append('\n');
        bool first = true;
        foreach (var block in blocks)
        {
            if (string.IsNullOrWhiteSpace(block)) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(block.TrimEnd('\n')).Append('\n');
            first = false;
        }
        return first && sb.Length == 0 ? "" : sb.ToString();
    }

    // ── Text helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Literal text, neutralised so AsciiDoc shows these characters. One exception: an apostrophe
    /// inside a word stays plain (<c>Don't</c>, not the Word importer's <c>Don+++'+++t</c>) and so
    /// renders typographic.
    /// </summary>
    public static string Inline(string text, bool inTableCell = false) => AsciidocText.EscapeInline(text, inTableCell).Replace("+++'+++", "'");

    /// <summary>Block text whose first line must not read as a block marker.</summary>
    public static string BlockStart(string text) => AsciidocText.EscapeBlockStart(text);

    /// <summary>A delimited block's delimiter, longer than any line of <paramref name="content"/> made of the same character.</summary>
    public static string Delimiter(char c, string content, int minimum = 4)
    {
        int longest = 0;
        foreach (var line in content.Split('\n'))
        {
            var t = line.TrimEnd('\r');
            if (t.Length >= minimum && t.All(ch => ch == c)) longest = Math.Max(longest, t.Length);
        }
        return new string(c, longest >= minimum ? longest + 1 : minimum);
    }

    /// <summary>A macro target/attribute text: <c>]</c> escaped.</summary>
    public static string MacroText(string text) => text.Replace("]", "\\]");

    /// <summary>A link target usable in a macro (spaces encoded).</summary>
    public static string Target(string url) => url.Trim().Replace(" ", "%20").Replace("[", "%5B").Replace("]", "%5D");
}
