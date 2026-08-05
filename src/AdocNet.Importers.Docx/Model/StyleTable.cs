using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace AdocNet.Importers.Docx;

/// <summary>A single style definition from <c>word/styles.xml</c>.</summary>
internal sealed class DocxStyle
{
    public required string StyleId { get; init; }
    public string? Name { get; init; }
    public string? BasedOn { get; init; }
    public string? Type { get; init; }
    public int? OutlineLevel { get; init; }
    public XElement? ParagraphProperties { get; init; }
    public XElement? RunProperties { get; init; }

    /// <summary>numId this paragraph style attaches to, when it declares one.</summary>
    public string? NumId { get; init; }
}

/// <summary>
/// Style lookup over <c>word/styles.xml</c>: resolves a paragraph's style to a
/// heading level, to the built-in semantic styles the importer recognises, and
/// walks <c>w:basedOn</c> chains so derived styles inherit their base's meaning.
/// </summary>
internal sealed class StyleTable
{
    private readonly Dictionary<string, DocxStyle> _byId = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DocxStyle> _byName = new(StringComparer.OrdinalIgnoreCase);

    public static StyleTable Empty { get; } = new();

    public static StyleTable Load(XDocument? stylesPart)
    {
        var table = new StyleTable();
        if (stylesPart?.Root is null) return table;

        foreach (var styleElement in stylesPart.Root.Elements(Ns.W + "style"))
        {
            var id = styleElement.Attribute(Ns.W + "styleId")?.Value;
            if (id is null) continue;

            var pPr = styleElement.Element(Ns.W + "pPr");
            var outline = pPr?.Element(Ns.W + "outlineLvl").WVal();
            var style = new DocxStyle
            {
                StyleId = id,
                Name = styleElement.Element(Ns.W + "name").WVal(),
                BasedOn = styleElement.Element(Ns.W + "basedOn").WVal(),
                Type = styleElement.Attribute(Ns.W + "type")?.Value,
                OutlineLevel = int.TryParse(outline, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lvl) ? lvl : null,
                ParagraphProperties = pPr,
                RunProperties = styleElement.Element(Ns.W + "rPr"),
                NumId = pPr?.Element(Ns.W + "numPr")?.Element(Ns.W + "numId").WVal(),
            };

            table._byId[id] = style;
            if (style.Name is not null) table._byName[style.Name] = style;
        }

        return table;
    }

    public DocxStyle? ById(string? styleId)
        => styleId is not null && _byId.TryGetValue(styleId, out var style) ? style : null;

    public DocxStyle? ByName(string name)
        => _byName.TryGetValue(name, out var style) ? style : null;

    /// <summary>
    /// Canonical name for a style id: the <c>w:name</c> when present, else the
    /// id itself. Word writes ids like <c>Heading1</c> with name
    /// <c>heading 1</c>; documents from other producers sometimes carry only
    /// one of the two, so both spellings are normalised here.
    /// </summary>
    public string? CanonicalName(string? styleId)
    {
        var style = ById(styleId);
        return style?.Name ?? styleId;
    }

    /// <summary>
    /// Heading level 1..9 for a style id, or null when the style is not a
    /// heading. Resolution order: built-in <c>heading N</c> name, then
    /// <c>w:outlineLvl</c>, then the <c>w:basedOn</c> chain.
    /// </summary>
    public int? HeadingLevel(string? styleId)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = ById(styleId);
        var rawId = styleId;

        while (true)
        {
            var level = HeadingLevelFromName(current?.Name) ?? HeadingLevelFromName(rawId);
            if (level is not null) return level;

            if (current?.OutlineLevel is int outline && outline >= 0 && outline <= 8)
            {
                // Only treat an outline level as a heading when the style is
                // not one of the body styles that legitimately carry one
                // (Word gives "Title" outlineLvl 0 as well).
                if (!IsNonHeadingOutlineStyle(current.Name ?? current.StyleId))
                    return outline + 1;
            }

            if (current?.BasedOn is null || !visited.Add(current.StyleId)) return null;
            rawId = current.BasedOn;
            current = ById(current.BasedOn);
            if (current is null) return HeadingLevelFromName(rawId);
        }
    }

    private static bool IsNonHeadingOutlineStyle(string name)
        => name.Equals("Title", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Subtitle", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Parses <c>heading 3</c> / <c>Heading3</c>. Localised aliases are out of
    /// scope: Word writes the English built-in name into <c>w:name</c> even in
    /// localised UIs.
    /// </summary>
    private static int? HeadingLevelFromName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var trimmed = name!.Trim();
        const string prefix = "heading";
        if (!trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;

        var rest = trimmed.Substring(prefix.Length).Trim();
        if (rest.Length == 0) return null;
        return int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level)
               && level >= 1 && level <= 9
            ? level
            : null;
    }

    /// <summary>
    /// True when <paramref name="styleId"/> is, or derives from, a style whose
    /// canonical name matches <paramref name="name"/>.
    /// </summary>
    public bool IsOrDerivesFrom(string? styleId, string name)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = ById(styleId);
        var rawId = styleId;

        while (true)
        {
            if (Matches(current?.Name, name) || Matches(rawId, name)) return true;
            if (current?.BasedOn is null || !visited.Add(current.StyleId)) return false;
            rawId = current.BasedOn;
            current = ById(current.BasedOn);
            if (current is null) return Matches(rawId, name);
        }
    }

    /// <summary>
    /// Style names compare ignoring case and internal spaces, so
    /// <c>IntenseQuote</c>, <c>Intense Quote</c> and <c>intensequote</c> are
    /// the same style.
    /// </summary>
    private static bool Matches(string? candidate, string name)
    {
        if (candidate is null) return false;
        return string.Equals(Squash(candidate), Squash(name), StringComparison.OrdinalIgnoreCase);
    }

    internal static string Squash(string value)
    {
        var chars = new char[value.Length];
        var length = 0;
        foreach (var c in value)
        {
            if (c == ' ' || c == '-' || c == '_') continue;
            chars[length++] = c;
        }

        return new string(chars, 0, length);
    }

    /// <summary>
    /// Effective run properties contributed by a paragraph or character style,
    /// walking the <c>w:basedOn</c> chain from the most-derived style first.
    /// </summary>
    public IEnumerable<XElement> RunPropertyChain(string? styleId)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = ById(styleId);
        while (current is not null && visited.Add(current.StyleId))
        {
            if (current.RunProperties is not null) yield return current.RunProperties;
            current = ById(current.BasedOn);
        }
    }

    /// <summary>Paragraph properties contributed by the style chain, most-derived first.</summary>
    public IEnumerable<XElement> ParagraphPropertyChain(string? styleId)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = ById(styleId);
        while (current is not null && visited.Add(current.StyleId))
        {
            if (current.ParagraphProperties is not null) yield return current.ParagraphProperties;
            current = ById(current.BasedOn);
        }
    }

    /// <summary>numId declared by a paragraph style (or an ancestor of it).</summary>
    public string? StyleNumId(string? styleId)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = ById(styleId);
        while (current is not null && visited.Add(current.StyleId))
        {
            if (current.NumId is not null) return current.NumId;
            current = ById(current.BasedOn);
        }

        return null;
    }
}
