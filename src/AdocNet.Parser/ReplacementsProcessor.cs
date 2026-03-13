using System.Globalization;
using System.Text;

namespace AdocNet.Parser;

/// <summary>
/// AsciiDoc Phase 4 replacements: symbol substitutions (<c>(C)</c> → ©, <c>-></c> → →)
/// and character entity resolution (<c>&amp;#169;</c>, <c>&amp;#xa0;</c>, <c>&amp;amp;</c>).
/// </summary>
internal static class ReplacementsProcessor
{
    private static readonly Dictionary<string, string> NamedEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["amp"] = "&",
        ["lt"] = "<",
        ["gt"] = ">",
        ["nbsp"] = "\u00A0",
        ["quot"] = "\"",
        ["apos"] = "'",
        ["copy"] = "\u00A9",
        ["reg"] = "\u00AE",
        ["trade"] = "\u2122",
        ["mdash"] = "\u2014",
        ["ndash"] = "\u2013",
        ["hellip"] = "\u2026",
        ["lsquo"] = "\u2018",
        ["rsquo"] = "\u2019",
        ["ldquo"] = "\u201C",
        ["rdquo"] = "\u201D",
        ["deg"] = "\u00B0",
        ["brvbar"] = "\u00A6",
        ["zwj"] = "\u200D",
    };

    /// <summary>
    /// Applies Phase 4 replacements to <paramref name="text"/>.
    /// Returns the original string if no replacements were made (zero allocation).
    /// </summary>
    public static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        bool hasParen = false, hasAmpersand = false, hasDash = false, hasLessThan = false, hasEquals = false;
        foreach (char c in text)
        {
            if (c == '(') hasParen = true;
            else if (c == '&') hasAmpersand = true;
            else if (c == '-') hasDash = true;
            else if (c == '<') hasLessThan = true;
            else if (c == '=') hasEquals = true;
        }
        if (!hasParen && !hasAmpersand && !hasDash && !hasLessThan && !hasEquals) return text;

        var sb = new StringBuilder(text.Length);
        int i = 0;

        while (i < text.Length)
        {
            char c = text[i];

            if (hasParen && c == '(' && i + 2 < text.Length)
            {
                if (text[i + 1] == 'C' && text[i + 2] == ')')
                { sb.Append('\u00A9'); i += 3; continue; }
                if (i + 3 < text.Length && text[i + 1] == 'T' && text[i + 2] == 'M' && text[i + 3] == ')')
                { sb.Append('\u2122'); i += 4; continue; }
                if (text[i + 1] == 'R' && text[i + 2] == ')')
                { sb.Append('\u00AE'); i += 3; continue; }
            }

            if (hasDash && c == '-' && i + 1 < text.Length && text[i + 1] == '>')
            { sb.Append('\u2192'); i += 2; continue; }
            if (hasLessThan && c == '<' && i + 1 < text.Length && text[i + 1] == '-')
            { sb.Append('\u2190'); i += 2; continue; }
            if (hasEquals && c == '=' && i + 1 < text.Length && text[i + 1] == '>')
            { sb.Append('\u21D2'); i += 2; continue; }
            if (hasLessThan && c == '<' && i + 1 < text.Length && text[i + 1] == '=')
            { sb.Append('\u21D0'); i += 2; continue; }

            if (hasAmpersand && c == '&' && i + 2 < text.Length)
            {
                var entityEnd = text.IndexOf(';', i + 1);
                if (entityEnd > i + 1 && entityEnd - i <= 10)
                {
                    var entityBody = text.AsSpan(i + 1, entityEnd - i - 1);
                    if (entityBody.Length >= 2 && entityBody[0] == '#')
                    {
                        if (entityBody.Length >= 3 && (entityBody[1] == 'x' || entityBody[1] == 'X'))
                        {
                            if (int.TryParse(entityBody[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hexCode)
                                && hexCode is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF))
                            { sb.Append(char.ConvertFromUtf32(hexCode)); i = entityEnd + 1; continue; }
                        }
                        else if (int.TryParse(entityBody[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var decCode)
                            && decCode is > 0 and <= 0x10FFFF and not (>= 0xD800 and <= 0xDFFF))
                        { sb.Append(char.ConvertFromUtf32(decCode)); i = entityEnd + 1; continue; }
                    }
                    else if (NamedEntities.TryGetValue(entityBody.ToString(), out var replacement))
                    { sb.Append(replacement); i = entityEnd + 1; continue; }
                }
            }

            sb.Append(c);
            i++;
        }

        return sb.ToString();
    }
}
