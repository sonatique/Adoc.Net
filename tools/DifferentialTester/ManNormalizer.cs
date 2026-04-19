using System.Text;
using System.Text.RegularExpressions;

namespace AdocNet.Tools.DifferentialTester;

/// <summary>
/// Normalizes roff man page output from both Asciidoctor and AdocNet to enable
/// meaningful comparison by stripping irrelevant structural differences.
/// </summary>
public static partial class ManNormalizer
{
    /// <summary>
    /// Normalizes man page (roff) output for comparison.
    /// </summary>
    public static string Normalize(string roff)
    {
        var result = roff;

        // 1. Normalize line endings
        result = result.Replace("\r\n", "\n").Replace("\r", "\n");

        // 2. Strip comment lines (Asciidoctor adds Title/Author/Generator/Date/Manual/Source/Language)
        result = StripCommentLines(result);

        // 3. Strip Asciidoctor-specific preamble macros
        //    Everything between .TH and the first .SH that is not content
        result = StripPreamble(result);

        // 4. Normalize font escape suffix: \fP → \fR (both mean "return to previous/regular font")
        result = result.Replace("\\fP", "\\fR");

        // 4b. Normalize \f(CR (constant-width roman) → \fB (bold, used for monospace in AdocNet)
        result = result.Replace("\\f(CR", "\\fB");

        // 4c. Strip doubled font changes like \fB\fB → \fB
        result = DoubledFontRegex().Replace(result, "$1");

        // 5. Normalize ellipsis: .\|.\|. → ...  and … → ...
        result = result.Replace(".\\|.\\|.", "...");
        result = result.Replace("\u2026", "...");

        // 6. Normalize smart typography
        result = NormalizeTypography(result);

        // 7. Normalize .SH quoting: .SH "NAME" and .SH NAME → .SH NAME
        result = SectionHeaderRegex().Replace(result, ".SH $1");

        // 8. Normalize paragraph macros: .sp → .PP (both introduce paragraph breaks)
        result = NormalizeParagraphMacros(result);

        // 9. Normalize option list format:
        //    Asciidoctor uses: .sp / content / .RS 4 / description / .RE
        //    AdocNet uses: .TP / content / description
        //    Normalize both to .TP form
        result = NormalizeOptionLists(result);

        // 10. Strip .TH date field (4th positional arg) — differs between implementations
        result = StripThDate(result);

        // 11. Normalize escaped hyphens: \- → - in content (both are valid)
        result = result.Replace("\\-", "-");

        // 12. Strip trailing \& (non-breaking zero-width space, Asciidoctor uses it)
        result = result.Replace("\\&", "");

        // 13. Strip AUTHOR and SEE ALSO sections (may differ structurally)
        result = StripAuthorSection(result);

        // 14. Strip conditional roff macros: .if n .RS 4, .if n .RE, .fam C, .fam
        result = StripConditionalMacros(result);

        // 15. Strip .PP directly after .SH (Asciidoctor omits it, AdocNet adds it for NAME section)
        result = StripPpAfterSh(result);

        // 16. Remove blank lines
        result = BlankLinesRegex().Replace(result, "\n");

        // 17. Trim lines
        result = TrimLines(result);

        // 18. Final trim
        result = result.Trim();

        return result;
    }

    private static string StripCommentLines(string roff)
    {
        var sb = new StringBuilder(roff.Length);
        foreach (var line in SplitLines(roff))
        {
            if (line.StartsWith(".\\\"") || line.StartsWith("'\\\""))
                continue;
            sb.Append(line);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Strips the Asciidoctor preamble: everything between the .TH line and the first .SH line.
    /// This includes .ie/.el, .ss, .nh, .ad, macro definitions (.de ... ..), .als, .if blocks, etc.
    /// The .TH line itself is preserved. The first .SH and all subsequent content is preserved.
    /// </summary>
    private static string StripPreamble(string roff)
    {
        var sb = new StringBuilder(roff.Length);
        bool foundTh = false;
        bool foundFirstSh = false;

        foreach (var line in SplitLines(roff))
        {
            if (!foundTh)
            {
                sb.Append(line);
                sb.Append('\n');
                if (line.StartsWith(".TH "))
                    foundTh = true;
                continue;
            }

            if (!foundFirstSh)
            {
                if (line.StartsWith(".SH "))
                    foundFirstSh = true;
                else
                    continue;
            }

            sb.Append(line);
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Normalizes paragraph macros: standalone .sp lines become .PP lines.
    /// Both represent paragraph breaks in roff.
    /// </summary>
    private static string NormalizeParagraphMacros(string roff)
    {
        var sb = new StringBuilder(roff.Length);
        foreach (var line in SplitLines(roff))
        {
            if (line == ".sp")
                sb.Append(".PP");
            else
                sb.Append(line);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Normalizes option list formatting.
    /// Asciidoctor uses: content, .RS 4, description, .RE
    /// AdocNet uses: .TP, content, description
    /// We normalize to the .TP form by converting .RS/.RE blocks to .TP.
    /// </summary>
    private static string NormalizeOptionLists(string roff)
    {
        var lines = SplitLines(roff);
        var result = new List<string>(lines.Length);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Detect .RS N pattern (start of indented block in option list context)
            if (line.StartsWith(".RS"))
                continue;

            if (line == ".RE")
                continue;

            // Check if this line is an option term followed by .RS
            // Pattern: .PP / \fB-option\fR / .RS 4 / description / .RE
            // Convert to: .TP / \fB-option\fR / description
            if (line == ".PP" && i + 2 < lines.Length)
            {
                var nextLine = lines[i + 1];
                var afterNext = lines[i + 2];
                if (nextLine.StartsWith("\\fB") && afterNext.StartsWith(".RS"))
                {
                    result.Add(".TP");
                    continue;
                }
            }

            result.Add(line);
        }

        var sb = new StringBuilder(roff.Length);
        foreach (var line in result)
        {
            sb.Append(line);
            sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Strips the date field from the .TH macro.
    /// .TH "CMD" "1" "2026-04-16" "Source" "Manual" → .TH "CMD" "1" "" "Source" "Manual"
    /// </summary>
    private static string StripThDate(string roff)
    {
        return ThDateRegex().Replace(roff, m =>
        {
            var prefix = m.Groups[1].Value;
            var suffix = m.Groups[2].Value;
            return $"{prefix} \"\"{suffix}";
        });
    }

    /// <summary>
    /// Strips the AUTHOR section that Asciidoctor adds but AdocNet doesn't.
    /// </summary>
    private static string StripAuthorSection(string roff)
    {
        var sb = new StringBuilder(roff.Length);
        bool inAuthorSection = false;

        foreach (var line in SplitLines(roff))
        {
            if (line == ".SH AUTHOR" || line == ".SH \"AUTHOR\"")
            {
                inAuthorSection = true;
                continue;
            }

            if (inAuthorSection && line.StartsWith(".SH "))
                inAuthorSection = false;

            if (!inAuthorSection)
            {
                sb.Append(line);
                sb.Append('\n');
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Strips conditional roff macros that differ between implementations:
    /// - .if n .RS N / .if n .RE (conditional indentation for nroff terminals)
    /// - .fam C / .fam (font family switching for code blocks)
    /// </summary>
    private static string StripConditionalMacros(string roff)
    {
        var sb = new StringBuilder(roff.Length);
        foreach (var line in SplitLines(roff))
        {
            // Strip .if n .RS N and .if n .RE
            if (line.StartsWith(".if n .RS") || line == ".if n .RE")
                continue;

            // Strip .fam C and bare .fam (font family reset)
            if (line.StartsWith(".fam"))
                continue;

            sb.Append(line);
            sb.Append('\n');
        }

        // Second pass: strip .PP that appears right before .nf (code block)
        var result = sb.ToString();
        sb.Clear();
        var cleaned = SplitLines(result);
        for (int i = 0; i < cleaned.Length; i++)
        {
            if (cleaned[i] == ".PP" && i + 1 < cleaned.Length && cleaned[i + 1] == ".nf")
                continue;
            sb.Append(cleaned[i]);
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>
    /// Strips .PP lines that appear immediately after .SH lines.
    /// Asciidoctor omits the .PP after .SH NAME, AdocNet adds it.
    /// </summary>
    private static string StripPpAfterSh(string roff)
    {
        var sb = new StringBuilder(roff.Length);
        var lines = SplitLines(roff);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Skip .PP if previous non-empty line was .SH
            if (line == ".PP" && i > 0)
            {
                // Look back for the previous non-blank line
                int prev = i - 1;
                while (prev >= 0 && lines[prev].Length == 0) prev--;
                if (prev >= 0 && lines[prev].StartsWith(".SH "))
                    continue;
            }

            sb.Append(line);
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static string TrimLines(string roff)
    {
        var sb = new StringBuilder(roff.Length);
        bool first = true;
        foreach (var line in SplitLines(roff))
        {
            if (!first) sb.Append('\n');
            sb.Append(line.TrimEnd());
            first = false;
        }
        return sb.ToString();
    }

    private static string NormalizeTypography(string roff)
    {
        roff = roff.Replace("\u2018", "'");
        roff = roff.Replace("\u2019", "'");
        roff = roff.Replace("\u201C", "\"");
        roff = roff.Replace("\u201D", "\"");
        roff = roff.Replace("\u2014", "--");
        roff = roff.Replace("\u2013", "-");
        roff = roff.Replace("\u200B", "");
        roff = roff.Replace("\\(aq", "'");
        roff = roff.Replace("\\(dq", "\"");
        return roff;
    }

    /// <summary>
    /// Splits a string into lines using \n as the delimiter.
    /// Returns an array of lines without the delimiter.
    /// </summary>
    private static string[] SplitLines(string text) => text.Split('\n');

    [GeneratedRegex(@"(\\f[BIR])\1+")]
    private static partial Regex DoubledFontRegex();

    [GeneratedRegex(@"\.SH ""([^""]*)""")]
    private static partial Regex SectionHeaderRegex();

    [GeneratedRegex(@"\n{2,}")]
    private static partial Regex BlankLinesRegex();

    /// <summary>Matches the .TH line and captures prefix before date and suffix after date.</summary>
    [GeneratedRegex(@"(\.TH\s+""[^""]*""\s+""[^""]*"")\s+""[^""]*""(.*)")]
    private static partial Regex ThDateRegex();
}
