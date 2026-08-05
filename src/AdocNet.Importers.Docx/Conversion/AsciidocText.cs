using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AdocNet.Importers.Docx;

/// <summary>
/// Escapes literal text so that re-parsing the emitted AsciiDoc yields exactly
/// the characters that were in the Word document.
/// <para>
/// Escaping is <em>match-driven</em>, not character-driven: a backslash is
/// inserted only where a substitution would actually fire, because AsciiDoc
/// keeps a backslash that does not suppress anything. <c>2 * 3</c> is left
/// alone; <c>a *b* c</c> gets its opening delimiter escaped.
/// </para>
/// </summary>
internal static class AsciidocText
{
    private const RegexOptions Opts = RegexOptions.CultureInvariant;

    // Each pattern captures the construct to neutralise in the "esc" group;
    // a backslash goes in immediately before that group.
    private static readonly Regex[] InlinePatterns =
    {
        // Unconstrained quote pairs (**bold**, __em__, ``mono``, ##mark##).
        new(@"(?<esc>\*\*.+?\*\*)", Opts),
        new(@"(?<esc>__.+?__)", Opts),
        new(@"(?<esc>``.+?``)", Opts),
        new(@"(?<esc>##.+?##)", Opts),

        // Constrained quote pairs: opening delimiter preceded by start of
        // string or a non-word character, closing delimiter not followed by a
        // word character — mirrors Asciidoctor's constrained-quote rule.
        new(@"(?:^|(?<=[^\w;:}\\]))(?<esc>\*\S(?:[^\n]*?\S)?\*)(?!\w)", Opts),
        new(@"(?:^|(?<=[^\w;:}\\]))(?<esc>_\S(?:[^\n]*?\S)?_)(?!\w)", Opts),
        new(@"(?:^|(?<=[^\w;:}\\]))(?<esc>`\S(?:[^\n]*?\S)?`)(?!\w)", Opts),
        new(@"(?:^|(?<=[^\w;:}\\]))(?<esc>#\S(?:[^\n]*?\S)?#)(?!\w)", Opts),
        new(@"(?:^|(?<=[^\w;:}\\]))(?<esc>\+\S(?:[^\n]*?\S)?\+)(?!\w)", Opts),

        // Super/subscript are unconstrained in AsciiDoc.
        new(@"(?<esc>\^\S+?\^)", Opts),
        new(@"(?<esc>~\S+?~)", Opts),

        // Triple-plus passthrough.
        new(@"(?<esc>\+{3}.*?\+{3})", Opts),

        // Attribute reference, cross reference, inline anchor.
        new(@"(?<esc>\{[A-Za-z0-9_][A-Za-z0-9_-]*\})", Opts),
        new(@"(?<esc><<[^<>\n]+>>)", Opts),
        new(@"(?<esc>\[\[[^\[\]\n]+\]\])", Opts),

        // Inline macros with an attribute list.
        new(@"(?:^|(?<=\W))(?<esc>(?:link|image|footnote|footnoteref|xref|kbd|btn|menu|icon|indexterm|stem|latexmath|asciimath|pass|mailto):[^\s\[]*\[[^\]\n]*\])", Opts),

        // Bare URLs auto-link; the visible text survives but the structure
        // would not, so they are escaped unless the caller asked for a link.
        new(@"(?:^|(?<=[\s(\[<]))(?<esc>(?:https?|ftp|irc):\/\/[^\s\[\]<>]+)", Opts),

        // Callout markers at end of line.
        new(@"(?<esc><\d+>)\s*$", Opts),

        // Text replacements.
        new(@"(?<esc>\((?:C|R|TM)\))", Opts),
        new(@"(?<esc>\.\.\.)", Opts),
        new(@"(?<esc>(?:->|<-|=>|<=))", Opts),
        new(@"(?<=\w)(?<esc>--)(?=\w)", Opts),
        new(@"(?:^|(?<= ))(?<esc>--)(?= |$)", Opts),
        new(@"(?<=\w)(?<esc>')(?=\w)", Opts),
    };

    // Block markers that would turn paragraph text into a different block if
    // they opened a line. Matched against the start of the first line only.
    private static readonly Regex LineStartMarkers = new(
        @"^(?:=+ |\*+ |-+ |\.+ |\d+\. |[a-zA-Z]\. |[ivxIVX]+\) |//|\[|:\S|\||\+$|<<<|'''|\.{4,}$|-{4,}$|={4,}$|_{4,}$|\*{4,}$|--$|/{4,}$)",
        Opts);

    /// <summary>
    /// Escapes inline text. <paramref name="insideTableCell"/> additionally
    /// escapes the cell separator.
    /// </summary>
    public static string EscapeInline(string text, bool insideTableCell = false)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var escaped = InsertEscapes(text);
        if (insideTableCell) escaped = escaped.Replace("|", "\\|");
        return escaped;
    }

    /// <summary>
    /// Escapes the first line of a block's text when it would otherwise be
    /// read as a block marker (list bullet, heading, attribute entry, …).
    /// </summary>
    public static string EscapeBlockStart(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return LineStartMarkers.IsMatch(text) ? "\\" + text : text;
    }

    private static string InsertEscapes(string text)
    {
        List<(int Start, int End)>? spans = null;

        foreach (var pattern in InlinePatterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var group = match.Groups["esc"];
                if (!group.Success) continue;
                (spans ??= new List<(int, int)>()).Add((group.Index, group.Index + group.Length));
            }
        }

        if (spans is null) return text;

        // Keep only outermost, earliest spans: escaping the opening delimiter
        // of the enclosing construct already neutralises everything nested in
        // it, and a second backslash inside would print literally.
        spans.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.End.CompareTo(a.End));

        var escapeAt = new List<int>();
        var coveredTo = -1;
        foreach (var span in spans)
        {
            if (span.Start < coveredTo) continue;
            escapeAt.Add(span.Start);
            coveredTo = span.End;
        }

        var sb = new StringBuilder(text.Length + escapeAt.Count);
        var next = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (next < escapeAt.Count && escapeAt[next] == i)
            {
                sb.Append('\\');
                next++;
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Turns arbitrary text into an AsciiDoc id: lowercase, non-alphanumerics
    /// collapsed to <c>_</c>, leading digit prefixed. Word bookmark names are
    /// already id-safe, but headings promoted to ids are not.
    /// </summary>
    public static string ToId(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lastWasSeparator = false;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator && sb.Length > 0)
            {
                sb.Append('_');
                lastWasSeparator = true;
            }
        }

        while (sb.Length > 0 && sb[sb.Length - 1] == '_') sb.Length--;
        if (sb.Length == 0) return "_";
        if (char.IsDigit(sb[0])) sb.Insert(0, '_');
        return sb.ToString();
    }
}
