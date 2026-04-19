using System.Text;
using System.Text.RegularExpressions;

namespace AdocNet.Tools.DifferentialTester;

/// <summary>
/// Normalizes XML output (DocBook 5) from both Asciidoctor and AdocNet to enable
/// meaningful comparison by stripping irrelevant structural differences.
/// </summary>
public static partial class XmlNormalizer
{
    /// <summary>
    /// Normalizes DocBook XML for comparison.
    /// </summary>
    public static string Normalize(string xml, XmlSource source = XmlSource.Unknown)
    {
        var result = xml;

        // 1. Normalize line endings
        result = result.Replace("\r\n", "\n").Replace("\r", "\n");

        // 2. Strip XML declaration (encoding differences)
        result = XmlDeclarationRegex().Replace(result, "");

        // 3. Strip processing instructions (<?asciidoc-toc?>, <?asciidoc-numbered?>)
        result = ProcessingInstructionRegex().Replace(result, "");

        // 4. Normalize namespace declarations on root element
        //    Both use http://docbook.org/ns/docbook and http://www.w3.org/1999/xlink
        //    but with different prefixes (xl: vs xlink:)
        result = NormalizeNamespaces(result);

        // 5. Strip xml:lang attribute (Asciidoctor adds it, AdocNet doesn't)
        result = XmlLangRegex().Replace(result, "");

        // 6. Strip <info> block (Asciidoctor adds author/date metadata)
        result = InfoBlockRegex().Replace(result, "");

        // 6b. Strip document-level <title> that is a direct child of <article>
        //     Asciidoctor puts it inside <info> (already stripped), AdocNet puts it standalone.
        //     Stripping both avoids false diffs on document title placement.
        result = ArticleTitleRegex().Replace(result, "");

        // 7. Normalize <simpara> to <para> (Asciidoctor uses simpara, AdocNet uses para)
        //    Must handle attributes too: <simpara xml:id="..."> → <para xml:id="...">
        result = SimparaOpenRegex().Replace(result, "<para$1>");
        result = result.Replace("</simpara>", "</para>");

        // 8. Strip linenumbering attribute on programlisting
        result = LinenumberingRegex().Replace(result, "");

        // 8b. Strip table attributes that differ structurally
        //     Asciidoctor adds colsep, frame, rowsep on tables; AdocNet doesn't
        result = ColsepRegex().Replace(result, "");
        result = FrameRegex().Replace(result, "");
        result = RowsepRegex().Replace(result, "");
        // Strip colname attributes on colspec (Asciidoctor adds col_1, col_2 etc.)
        result = ColnameRegex().Replace(result, "");
        // Normalize colwidth: "50*" and "1*" both represent proportional widths
        // Leave as-is — this is a real content difference worth tracking

        // 8c. Normalize self-closing tag whitespace: <tag /> → <tag/>
        result = SelfClosingSpaceRegex().Replace(result, "/>");

        // 8d. Normalize colspec colwidth to proportional form
        //     Both "50*" (percentage) and "1*" (ratio) represent equal widths.
        //     Normalize to consistent form by stripping the numeric value.
        result = ColwidthRegex().Replace(result, "colwidth=\"*\"");

        // 9. Sort attributes within each tag
        result = SortXmlAttributes(result);

        // 10. Normalize whitespace in text nodes (collapse runs of whitespace)
        result = NormalizeTextWhitespace(result);

        // 10b. Collapse whitespace between closing/opening tags
        //      ></whitespace>< → >< (normalizes indentation differences)
        //      But preserve content inside pre-formatted elements
        result = CollapseInterTagWhitespace(result);

        // 11. Remove blank lines
        result = BlankLinesRegex().Replace(result, "\n");

        // 12. Trim lines
        result = TrimLines(result);

        // 13. Normalize smart typography to ASCII
        result = NormalizeTypography(result);

        // 14. Final trim
        result = result.Trim();

        return result;
    }

    private static string NormalizeNamespaces(string xml)
    {
        // Normalize xlink namespace prefix: xl: → xlink: and xmlns:xl → xmlns:xlink
        xml = xml.Replace("xmlns:xl=", "xmlns:xlink=");
        xml = xml.Replace("xl:", "xlink:");
        return xml;
    }

    private static string SortXmlAttributes(string xml)
    {
        return XmlTagWithAttrsRegex().Replace(xml, match =>
        {
            var tagName = match.Groups[1].Value;
            var attrsString = match.Groups[2].Value.Trim();
            var closing = match.Groups[3].Value;

            if (string.IsNullOrWhiteSpace(attrsString))
                return match.Value;

            var attrs = XmlAttributeRegex().Matches(attrsString)
                .Select(m => m.Value.Trim())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (attrs.Count == 0)
                return match.Value;

            return $"<{tagName} {string.Join(" ", attrs)}{closing}>";
        });
    }

    private static string CollapseInterTagWhitespace(string xml)
    {
        var sb = new StringBuilder(xml.Length);
        bool inPreformatted = false;

        for (int i = 0; i < xml.Length; i++)
        {
            char c = xml[i];

            // Track preformatted elements
            if (c == '<')
            {
                var tag = ExtractTagName(xml, i);
                if (tag is "programlisting" or "screen" or "literallayout")
                    inPreformatted = true;
                else if (tag is "/programlisting" or "/screen" or "/literallayout")
                    inPreformatted = false;
            }

            if (inPreformatted)
            {
                sb.Append(c);
                continue;
            }

            // When we see > followed by whitespace followed by <, collapse it
            if (c == '>')
            {
                sb.Append(c);
                // Look ahead: skip whitespace-only text before next tag
                int j = i + 1;
                while (j < xml.Length && (xml[j] == ' ' || xml[j] == '\t' || xml[j] == '\n' || xml[j] == '\r'))
                    j++;
                if (j < xml.Length && xml[j] == '<')
                    i = j - 1; // Skip the whitespace
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string? ExtractTagName(string xml, int pos)
    {
        if (pos >= xml.Length || xml[pos] != '<')
            return null;
        int start = pos + 1;
        int end = start;
        while (end < xml.Length && xml[end] != '>' && xml[end] != ' ' && xml[end] != '/')
            end++;
        return end > start ? xml[start..end] : null;
    }

    private static string NormalizeTextWhitespace(string xml)
    {
        var sb = new StringBuilder(xml.Length);
        bool inTag = false;
        bool inProgramlisting = false;
        bool lastWasSpace = false;

        for (int i = 0; i < xml.Length; i++)
        {
            char c = xml[i];

            if (c == '<')
            {
                inTag = true;
                lastWasSpace = false;
                sb.Append(c);

                // Check for <programlisting or </programlisting
                if (i + 15 < xml.Length && xml.AsSpan(i + 1, 14).StartsWith("programlisting"))
                    inProgramlisting = true;
                else if (i + 16 < xml.Length && xml.AsSpan(i + 1, 15).StartsWith("/programlisting"))
                    inProgramlisting = false;

                continue;
            }

            if (c == '>')
            {
                inTag = false;
                lastWasSpace = false;
                sb.Append(c);
                continue;
            }

            if (inTag || inProgramlisting)
            {
                sb.Append(c);
                lastWasSpace = false;
                continue;
            }

            if (c is ' ' or '\t')
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
                continue;
            }

            lastWasSpace = c == '\n';
            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string TrimLines(string xml)
    {
        var lines = xml.Split('\n');
        var sb = new StringBuilder(xml.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(lines[i].Trim());
        }
        return sb.ToString();
    }

    private static string NormalizeTypography(string xml)
    {
        xml = xml.Replace("\u2018", "'");
        xml = xml.Replace("\u2019", "'");
        xml = xml.Replace("\u201C", "\"");
        xml = xml.Replace("\u201D", "\"");
        xml = xml.Replace("\u2014", "--");
        xml = xml.Replace("\u2013", "-");
        xml = xml.Replace("\u2026", "...");
        xml = xml.Replace("\u200B", "");
        xml = xml.Replace("\u2009", " ");
        // HTML/XML entities for smart typography
        xml = xml.Replace("&#8216;", "'");
        xml = xml.Replace("&#8217;", "'");
        xml = xml.Replace("&#8220;", "\"");
        xml = xml.Replace("&#8221;", "\"");
        xml = xml.Replace("&#8212;", "--");
        xml = xml.Replace("&#8211;", "-");
        xml = xml.Replace("&#8230;", "...");
        xml = xml.Replace("&#8203;", "");
        xml = xml.Replace("&#8201;", " ");
        return xml;
    }

    [GeneratedRegex(@"<\?xml\s[^?]*\?>")]
    private static partial Regex XmlDeclarationRegex();

    [GeneratedRegex(@"<\?asciidoc[^?]*\?>")]
    private static partial Regex ProcessingInstructionRegex();

    [GeneratedRegex(@"\s*xml:lang=""[^""]*""")]
    private static partial Regex XmlLangRegex();

    [GeneratedRegex(@"<info>.*?</info>\s*", RegexOptions.Singleline)]
    private static partial Regex InfoBlockRegex();

    /// <summary>Matches a standalone title element that follows the article opening tag.</summary>
    [GeneratedRegex(@"(?<=<article[^>]*>)\s*<title>[^<]*</title>")]
    private static partial Regex ArticleTitleRegex();

    [GeneratedRegex(@"\s+linenumbering=""[^""]*""")]
    private static partial Regex LinenumberingRegex();

    [GeneratedRegex(@"\s+colsep=""[^""]*""")]
    private static partial Regex ColsepRegex();

    [GeneratedRegex(@"\s+frame=""[^""]*""")]
    private static partial Regex FrameRegex();

    [GeneratedRegex(@"\s+rowsep=""[^""]*""")]
    private static partial Regex RowsepRegex();

    [GeneratedRegex(@"\s+colname=""[^""]*""")]
    private static partial Regex ColnameRegex();

    [GeneratedRegex(@"\s+/>")]
    private static partial Regex SelfClosingSpaceRegex();

    [GeneratedRegex(@"colwidth=""[\d.]+\*""")]
    private static partial Regex ColwidthRegex();


    [GeneratedRegex(@"<(\w[\w:.-]*)((?:\s+[^>]*?)?)(\s*/?)>")]
    private static partial Regex XmlTagWithAttrsRegex();

    [GeneratedRegex(@"[\w:.-]+=""[^""]*""")]
    private static partial Regex XmlAttributeRegex();

    [GeneratedRegex(@"<simpara(\s[^>]*)?>", RegexOptions.IgnoreCase)]
    private static partial Regex SimparaOpenRegex();

    [GeneratedRegex(@"\n{2,}")]
    private static partial Regex BlankLinesRegex();
}

/// <summary>
/// Identifies the source engine for XML normalization.
/// </summary>
public enum XmlSource
{
    Unknown,
    Asciidoctor,
    AdocNet,
}
