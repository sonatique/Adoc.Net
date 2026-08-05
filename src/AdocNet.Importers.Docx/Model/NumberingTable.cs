using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;
using AdocNet.Ast;

namespace AdocNet.Importers.Docx;

/// <summary>Resolved numbering definition for one <c>numId</c> + <c>ilvl</c> pair.</summary>
internal sealed class NumberingLevel
{
    public required ListKind Kind { get; init; }

    /// <summary>AsciiDoc list style (<c>loweralpha</c>, <c>upperroman</c>, …), null for the default.</summary>
    public string? ListStyle { get; init; }

    /// <summary>Start value when the level does not start at 1.</summary>
    public int? Start { get; init; }

    /// <summary>Raw <c>w:numFmt</c> value, kept for reporting.</summary>
    public required string NumberFormat { get; init; }
}

/// <summary>
/// Reads <c>word/numbering.xml</c> and resolves (numId, ilvl) to a list kind.
/// Handles the <c>w:num</c> → <c>w:abstractNum</c> indirection, per-instance
/// <c>w:lvlOverride</c>, and the <c>w:numStyleLink</c> hop that Word uses for
/// multi-level list styles.
/// </summary>
internal sealed class NumberingTable
{
    private readonly Dictionary<string, XElement> _abstractNums = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _numToAbstract = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, XElement>> _overrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _styleLinks = new(StringComparer.OrdinalIgnoreCase);
    private StyleTable _styles = StyleTable.Empty;

    public static NumberingTable Empty { get; } = new();

    public static NumberingTable Load(XDocument? numberingPart, StyleTable styles)
    {
        var table = new NumberingTable { _styles = styles };
        if (numberingPart?.Root is null) return table;

        foreach (var abstractNum in numberingPart.Root.Elements(Ns.W + "abstractNum"))
        {
            var id = abstractNum.Attribute(Ns.W + "abstractNumId")?.Value;
            if (id is null) continue;
            table._abstractNums[id] = abstractNum;

            // A styleLink means "this abstract definition is the body of the
            // named list style"; numStyleLink points the other way.
            var styleLink = abstractNum.Element(Ns.W + "styleLink").WVal();
            if (styleLink is not null) table._styleLinks[styleLink] = id;
        }

        foreach (var num in numberingPart.Root.Elements(Ns.W + "num"))
        {
            var numId = num.Attribute(Ns.W + "numId")?.Value;
            var abstractId = num.Element(Ns.W + "abstractNumId").WVal();
            if (numId is null || abstractId is null) continue;
            table._numToAbstract[numId] = abstractId;

            foreach (var levelOverride in num.Elements(Ns.W + "lvlOverride"))
            {
                var ilvlText = levelOverride.Attribute(Ns.W + "ilvl")?.Value;
                var lvl = levelOverride.Element(Ns.W + "lvl");
                if (ilvlText is null || lvl is null) continue;
                if (!int.TryParse(ilvlText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ilvl)) continue;

                if (!table._overrides.TryGetValue(numId, out var map))
                    table._overrides[numId] = map = new Dictionary<int, XElement>();
                map[ilvl] = lvl;
            }
        }

        return table;
    }

    /// <summary>
    /// Resolves the level definition, or null when the numbering part does not
    /// describe this (numId, ilvl) pair. A missing definition is not fatal:
    /// callers fall back to an unordered list.
    /// </summary>
    public NumberingLevel? Resolve(string numId, int ilvl)
    {
        var lvl = FindLevelElement(numId, ilvl);
        if (lvl is null) return null;

        var numFmt = lvl.Element(Ns.W + "numFmt").WVal() ?? "bullet";
        var startText = lvl.Element(Ns.W + "start").WVal();
        int? start = int.TryParse(startText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed != 1
            ? parsed
            : null;

        var (kind, style) = MapFormat(numFmt);
        return new NumberingLevel
        {
            Kind = kind,
            ListStyle = style,
            // Only ordered lists carry a start value in AsciiDoc.
            Start = kind == ListKind.Ordered ? start : null,
            NumberFormat = numFmt,
        };
    }

    private XElement? FindLevelElement(string numId, int ilvl)
    {
        if (_overrides.TryGetValue(numId, out var overrides) && overrides.TryGetValue(ilvl, out var overridden))
            return overridden;

        if (!_numToAbstract.TryGetValue(numId, out var abstractId)) return null;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        while (abstractId is not null && visited.Add(abstractId))
        {
            if (!_abstractNums.TryGetValue(abstractId, out var abstractNum)) return null;

            foreach (var lvl in abstractNum.Elements(Ns.W + "lvl"))
            {
                var levelText = lvl.Attribute(Ns.W + "ilvl")?.Value;
                if (levelText is null) continue;
                if (int.TryParse(levelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var level) && level == ilvl)
                    return lvl;
            }

            // numStyleLink: the real levels live in the abstract definition
            // owned by the referenced list style.
            var numStyleLink = abstractNum.Element(Ns.W + "numStyleLink").WVal();
            if (numStyleLink is null) return null;

            abstractId = ResolveStyleLink(numStyleLink);
        }

        return null;
    }

    private string? ResolveStyleLink(string styleId)
    {
        if (_styleLinks.TryGetValue(styleId, out var abstractId)) return abstractId;

        // The link names a style id; the styleLink recorded above uses the
        // style id too, but some producers write the style *name* instead.
        var name = _styles.CanonicalName(styleId);
        if (name is not null && _styleLinks.TryGetValue(name, out abstractId)) return abstractId;

        // Last resort: the style itself may attach a numId we can follow.
        var numId = _styles.StyleNumId(styleId);
        if (numId is not null && _numToAbstract.TryGetValue(numId, out abstractId)) return abstractId;

        return null;
    }

    /// <summary>
    /// Maps a <c>w:numFmt</c> to an AsciiDoc list kind and style. Formats with
    /// no AsciiDoc equivalent (ordinal, cardinalText, chicago, …) fall back to
    /// a plain ordered list; the caller reports the approximation.
    /// </summary>
    internal static (ListKind Kind, string? ListStyle) MapFormat(string numFmt) => numFmt switch
    {
        "bullet" => (ListKind.Unordered, null),
        "none" => (ListKind.Unordered, null),
        "decimal" => (ListKind.Ordered, null),
        "decimalZero" => (ListKind.Ordered, null),
        "lowerLetter" => (ListKind.Ordered, "loweralpha"),
        "upperLetter" => (ListKind.Ordered, "upperalpha"),
        "lowerRoman" => (ListKind.Ordered, "lowerroman"),
        "upperRoman" => (ListKind.Ordered, "upperroman"),
        _ => (ListKind.Ordered, null),
    };

    /// <summary>True when the format has an exact AsciiDoc counterpart.</summary>
    internal static bool IsExactFormat(string numFmt) => numFmt switch
    {
        "bullet" or "decimal" or "lowerLetter" or "upperLetter" or "lowerRoman" or "upperRoman" => true,
        _ => false,
    };
}
