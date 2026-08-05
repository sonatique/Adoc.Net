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

    /// <summary>
    /// Resolves the formatting that should become inline markup.
    /// <para>
    /// Only direct run properties and the character style chain are consulted.
    /// Formatting a run inherits from its <em>paragraph</em> style is
    /// block-level presentation — Word's heading styles are bold, its Quote
    /// style is italic — and turning that into <c>*…*</c> or <c>_…_</c> would
    /// litter every heading with markup that says nothing about the author's
    /// intent. The paragraph style still decides monospacing, because that is
    /// what identifies a code paragraph.
    /// </para>
    /// </summary>
    public static RunFormat Resolve(XElement? directRunProperties, string? characterStyleId,
        string? paragraphStyleId, StyleTable styles)
    {
        // Search order: direct → character style chain.
        var chain = new List<XElement>();
        if (directRunProperties is not null) chain.Add(directRunProperties);
        foreach (var rPr in styles.RunPropertyChain(characterStyleId)) chain.Add(rPr);

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

        // The built-in Hyperlink styles are underlined and coloured. That is
        // how Word draws a link, not authored formatting: an AsciiDoc backend
        // styles links itself, and carrying the decoration into the link
        // macro's label would put role markup inside its attribute list.
        if (styles.IsOrDerivesFrom(characterStyleId, "Hyperlink")
            || styles.IsOrDerivesFrom(characterStyleId, "FollowedHyperlink"))
        {
            underline = null;
            color = null;
        }

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
        if (fonts is null)
        {
            // The paragraph style can carry the monospace font itself, which
            // is how a pasted code paragraph usually looks.
            foreach (var rPr in styles.RunPropertyChain(paragraphStyleId))
            {
                var styleFonts = rPr.Element(Ns.W + "rFonts");
                var styleAscii = styleFonts?.Attribute(Ns.W + "ascii")?.Value;
                if (styleAscii is not null && MonospaceFonts.Contains(styleAscii)) return true;
            }

            return false;
        }

        var ascii = fonts.Attribute(Ns.W + "ascii")?.Value
                    ?? fonts.Attribute(Ns.W + "hAnsi")?.Value
                    ?? fonts.Attribute(Ns.W + "cs")?.Value;
        return ascii is not null && MonospaceFonts.Contains(ascii);
    }
}
