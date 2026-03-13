using System.Text;
using System.Text.RegularExpressions;

namespace AdocNet.Tools.DifferentialTester;

/// <summary>
/// Normalizes HTML output from both Asciidoctor and AdocNet to enable
/// meaningful comparison by stripping irrelevant structural differences.
/// </summary>
public static partial class HtmlNormalizer
{
    /// <summary>
    /// Normalizes HTML for comparison: strips wrappers, normalizes whitespace,
    /// sorts attributes, and removes engine-specific artifacts.
    /// </summary>
    public static string Normalize(string html, HtmlSource source = HtmlSource.Unknown)
    {
        var result = html;

        // 1. Normalize line endings to LF
        result = result.Replace("\r\n", "\n").Replace("\r", "\n");

        // 2. Strip ALL <div> and </div> tags from both outputs.
        // Asciidoctor wraps nearly everything in <div class="..."> wrappers
        // (paragraph, sectionbody, sect1, ulist, olist, etc.) while AdocNet
        // doesn't use <div> at all. Stripping them entirely lets us compare
        // the semantic content underneath.
        result = DivOpenRegex().Replace(result, "");
        result = result.Replace("</div>", "");

        // 3. Strip the Asciidoctor "Last updated" footer
        result = LastUpdatedRegex().Replace(result, "");

        // 4. Normalize self-closing tags: <br/>, <br /> → <br>
        result = SelfClosingRegex().Replace(result, "<$1>");

        // 5. Sort attributes within each tag
        result = SortAttributesInTags(result);

        // 6. Collapse whitespace: multiple spaces/tabs → single space
        result = CollapseWhitespace(result);

        // 7. Remove blank lines
        result = BlankLinesRegex().Replace(result, "\n");

        // 8. Trim leading/trailing whitespace per line
        result = TrimLines(result);

        // 9. Normalize smart typography to ASCII equivalents.
        //    Both Asciidoctor and AdocNet emit curly quotes/dashes/ellipses
        //    by default, but they may differ in edge cases. Normalizing
        //    both to ASCII lets us compare semantic content.
        result = NormalizeSmartTypography(result);

        // 10. Final trim
        result = result.Trim();

        return result;
    }

    /// <summary>
    /// Sorts HTML attributes alphabetically within each tag for deterministic comparison.
    /// </summary>
    private static string SortAttributesInTags(string html)
    {
        return TagWithAttrsRegex().Replace(html, match =>
        {
            var tagName = match.Groups[1].Value;
            var attrsString = match.Groups[2].Value.Trim();
            var closing = match.Groups[3].Value;

            if (string.IsNullOrWhiteSpace(attrsString))
                return match.Value;

            var attrs = AttributeRegex().Matches(attrsString)
                .Select(m => m.Value.Trim())
                .OrderBy(a => a, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (attrs.Count == 0)
                return match.Value;

            // Sort class names within class attribute
            for (int i = 0; i < attrs.Count; i++)
            {
                var classMatch = ClassValueRegex().Match(attrs[i]);
                if (classMatch.Success)
                {
                    var classes = classMatch.Groups[1].Value
                        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .OrderBy(c => c, StringComparer.OrdinalIgnoreCase);
                    attrs[i] = $"class=\"{string.Join(" ", classes)}\"";
                }
            }

            return $"<{tagName} {string.Join(" ", attrs)}{closing}>";
        });
    }

    private static string CollapseWhitespace(string html)
    {
        var sb = new StringBuilder(html.Length);
        bool inTag = false;
        bool inPre = false;
        bool lastWasSpace = false;

        for (int i = 0; i < html.Length; i++)
        {
            char c = html[i];

            if (c == '<')
            {
                inTag = true;
                lastWasSpace = false;
                sb.Append(c);

                // Check for <pre or </pre
                if (i + 4 < html.Length && html.AsSpan(i + 1, 3).Equals("pre", StringComparison.OrdinalIgnoreCase)
                    && (html[i + 4] == '>' || html[i + 4] == ' '))
                    inPre = true;
                else if (i + 5 < html.Length && html.AsSpan(i + 1, 4).Equals("/pre", StringComparison.OrdinalIgnoreCase))
                    inPre = false;

                continue;
            }

            if (c == '>')
            {
                inTag = false;
                lastWasSpace = false;
                sb.Append(c);
                continue;
            }

            if (inTag || inPre)
            {
                sb.Append(c);
                lastWasSpace = false;
                continue;
            }

            // Outside tags and not in <pre>: collapse whitespace
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

    private static string TrimLines(string html)
    {
        var lines = html.Split('\n');
        var sb = new StringBuilder(html.Length);
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(lines[i].Trim());
        }
        return sb.ToString();
    }

    /// <summary>
    /// Replaces smart typography characters and their HTML entities with plain ASCII
    /// equivalents so that comparisons focus on semantic content rather than
    /// typographic niceties.
    /// </summary>
    private static string NormalizeSmartTypography(string html)
    {
        // Curly single quotes / apostrophes
        html = html.Replace("\u2018", "'");  // left single quote '
        html = html.Replace("\u2019", "'");  // right single quote / apostrophe '
        html = html.Replace("&#8216;", "'");
        html = html.Replace("&#8217;", "'");
        html = html.Replace("&#39;", "'");   // HTML-escaped straight apostrophe
        html = html.Replace("&lsquo;", "'");
        html = html.Replace("&rsquo;", "'");
        html = html.Replace("&apos;", "'");

        // Curly double quotes
        html = html.Replace("\u201C", "\""); // left double quote "
        html = html.Replace("\u201D", "\""); // right double quote "
        html = html.Replace("&#8220;", "\"");
        html = html.Replace("&#8221;", "\"");
        html = html.Replace("&#34;", "\"");  // HTML-escaped straight double quote
        html = html.Replace("&ldquo;", "\"");
        html = html.Replace("&rdquo;", "\"");
        html = html.Replace("&quot;", "\"");

        // Em dash
        html = html.Replace("\u2014", "--"); // —
        html = html.Replace("&#8212;", "--");
        html = html.Replace("&mdash;", "--");

        // En dash
        html = html.Replace("\u2013", "-");  // –
        html = html.Replace("&#8211;", "-");
        html = html.Replace("&ndash;", "-");

        // Ellipsis
        html = html.Replace("\u2026", "..."); // …
        html = html.Replace("&#8230;", "...");
        html = html.Replace("&hellip;", "...");

        // Zero-width spaces and thin spaces inserted by Asciidoctor
        // around dashes and after ellipses
        html = html.Replace("\u200B", "");   // zero-width space
        html = html.Replace("&#8203;", "");
        html = html.Replace("\u2009", " ");  // thin space → regular space
        html = html.Replace("&#8201;", " ");
        html = html.Replace("&thinsp;", " ");

        return html;
    }

    // ── Generated regex patterns ────────────────────────────────────────

    [GeneratedRegex(@"<(br|hr|img|input|meta|link|col|area|base|embed|param|source|track|wbr)\s*/\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex SelfClosingRegex();

    [GeneratedRegex(@"<(\w+)((?:\s+[^>]*?)?)(\s*/?)>")]
    private static partial Regex TagWithAttrsRegex();

    [GeneratedRegex(@"[\w-]+(?:=""[^""]*"")?|[\w-]+(?:='[^']*')?")]
    private static partial Regex AttributeRegex();

    [GeneratedRegex(@"class=""([^""]*)""")]
    private static partial Regex ClassValueRegex();

    [GeneratedRegex(@"\n{2,}")]
    private static partial Regex BlankLinesRegex();

    [GeneratedRegex(@"<div\s+id=""last-updated""[^>]*>.*?</div>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex LastUpdatedRegex();

    [GeneratedRegex(@"<div[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex DivOpenRegex();
}

/// <summary>
/// Identifies the source engine for HTML normalization hints.
/// </summary>
public enum HtmlSource
{
    Unknown,
    Asciidoctor,
    AdocNet,
}
