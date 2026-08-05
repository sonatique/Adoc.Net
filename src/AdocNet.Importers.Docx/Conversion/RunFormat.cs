using System;
using System.Collections.Generic;
using System.Xml.Linq;

namespace AdocNet.Importers.Docx;

/// <summary>Vertical alignment of a run.</summary>
internal enum RunVerticalAlign
{
    Baseline,
    Superscript,
    Subscript,
}

/// <summary>
/// Character formatting resolved for one run: direct <c>w:rPr</c> first, then
/// the character style chain, then the paragraph style chain. A direct
/// property that is explicitly off (<c>w:val="0"</c>) wins over an inherited
/// on, which is why resolution returns the first element found rather than
/// "any element found".
/// </summary>
internal readonly struct RunFormat
{
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Monospace { get; init; }
    public bool Underline { get; init; }
    public bool Strike { get; init; }
    public bool SmallCaps { get; init; }
    public bool AllCaps { get; init; }
    public bool Highlighted { get; init; }
    public RunVerticalAlign VerticalAlign { get; init; }

    /// <summary>Hex colour without the leading <c>#</c>, when set and not automatic.</summary>
    public string? Color { get; init; }

    /// <summary>True when nothing beyond plain text applies.</summary>
    public bool IsPlain => !Bold && !Italic && !Monospace && !Underline && !Strike
                           && !SmallCaps && !AllCaps && !Highlighted
                           && VerticalAlign == RunVerticalAlign.Baseline && Color is null;

    private static readonly HashSet<string> MonospaceFonts = new(StringComparer.OrdinalIgnoreCase)
    {
        "Consolas", "Courier", "Courier New", "Lucida Console", "Lucida Sans Typewriter",
        "Menlo", "Monaco", "Andale Mono", "DejaVu Sans Mono", "Liberation Mono",
        "Roboto Mono", "Source Code Pro", "Fira Code", "Fira Mono", "Cascadia Code",
        "Cascadia Mono", "Inconsolata", "SF Mono", "IBM Plex Mono", "JetBrains Mono",
        "monospace", "Nimbus Mono", "PT Mono", "Ubuntu Mono",
    };

    private static readonly HashSet<string> MonospaceStyleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Code", "HTMLCode", "HTMLTypewriter", "VerbatimChar", "SourceText", "MacroText",
        "PlainText", "HTMLPreformatted", "Preformatted", "CodeChar", "SourceCodeChar",
    };

    public static RunFormat Resolve(XElement? directRunProperties, string? characterStyleId,
        string? paragraphStyleId, StyleTable styles)
    {
        // Search order: direct → character style chain → paragraph style chain.
        var chain = new List<XElement>();
        if (directRunProperties is not null) chain.Add(directRunProperties);
        foreach (var rPr in styles.RunPropertyChain(characterStyleId)) chain.Add(rPr);
        foreach (var rPr in styles.RunPropertyChain(paragraphStyleId)) chain.Add(rPr);

        var vertAlign = First(chain, "vertAlign").WVal() switch
        {
            "superscript" => RunVerticalAlign.Superscript,
            "subscript" => RunVerticalAlign.Subscript,
            _ => RunVerticalAlign.Baseline,
        };

        var color = First(chain, "color").WVal();
        if (color is not null && (color.Equals("auto", StringComparison.OrdinalIgnoreCase) || color == "000000"))
            color = null;

        var underline = First(chain, "u");
        var highlight = First(chain, "highlight").WVal();
        var shading = First(chain, "shd")?.Attribute(Ns.W + "fill")?.Value;

        return new RunFormat
        {
            Bold = First(chain, "b").IsToggleOn(),
            Italic = First(chain, "i").IsToggleOn(),
            Underline = underline is not null && underline.WVal() != "none",
            Strike = First(chain, "strike").IsToggleOn() || First(chain, "dstrike").IsToggleOn(),
            SmallCaps = First(chain, "smallCaps").IsToggleOn(),
            AllCaps = First(chain, "caps").IsToggleOn(),
            Highlighted = (highlight is not null && highlight != "none")
                          || (shading is not null && !shading.Equals("auto", StringComparison.OrdinalIgnoreCase) && shading != "FFFFFF"),
            VerticalAlign = vertAlign,
            Color = color,
            Monospace = IsMonospace(chain, characterStyleId, paragraphStyleId, styles),
        };
    }

    private static XElement? First(List<XElement> chain, string localName)
    {
        foreach (var rPr in chain)
        {
            var element = rPr.Element(Ns.W + localName);
            if (element is not null) return element;
        }

        return null;
    }

    private static bool IsMonospace(List<XElement> chain, string? characterStyleId,
        string? paragraphStyleId, StyleTable styles)
    {
        foreach (var name in MonospaceStyleNames)
        {
            if (styles.IsOrDerivesFrom(characterStyleId, name)) return true;
            if (styles.IsOrDerivesFrom(paragraphStyleId, name)) return true;
        }

        var fonts = First(chain, "rFonts");
        if (fonts is null) return false;

        var ascii = fonts.Attribute(Ns.W + "ascii")?.Value
                    ?? fonts.Attribute(Ns.W + "hAnsi")?.Value
                    ?? fonts.Attribute(Ns.W + "cs")?.Value;
        return ascii is not null && MonospaceFonts.Contains(ascii);
    }
}
