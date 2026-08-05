using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace AdocNet.Importers.Docx;

/// <summary>
/// Neutralises literal text so that re-parsing the emitted AsciiDoc yields
/// exactly the characters that were in the Word document.
/// <para>
/// Two mechanisms are used, because AsciiDoc's backslash escape does not cover
/// every substitution class:
/// </para>
/// <list type="bullet">
///   <item><description><b>Backslash</b> for quote pairs, attribute
///     references, cross references, anchors, callouts and bare URLs — the
///     constructs whose leading delimiter the parser treats as escapable.</description></item>
///   <item><description><b>Triple-plus passthrough</b> for text replacements
///     ((C), --, …, arrows, apostrophes) and macro-shaped text, where a
///     backslash is not honoured and would itself show up in the output.
///     Passthroughs suppress the special-character substitution too, so a span
///     containing <c>&lt;</c>, <c>&gt;</c> or <c>&amp;</c> is wrapped only up
///     to the last such character, leaving it outside where it still gets
///     encoded.</description></item>
/// </list>
/// <para>
/// Neutralisation is match-driven: nothing is escaped unless a substitution
/// would actually fire, so <c>2 * 3</c> stays as written.
/// </para>
/// </summary>
internal static class AsciidocText
{
    private const RegexOptions Opts = RegexOptions.CultureInvariant;
    private const string PassOpen = "+++";
    private const string PassClose = "+++";

    private enum Neutralisation
    {
        /// <summary>Insert a backslash before the span.</summary>
        Backslash,

        /// <summary>Wrap the span in an inline passthrough.</summary>
        Passthrough,
    }

    private static readonly (Regex Pattern, Neutralisation Mode)[] InlinePatterns =
    {
        // ── Quote pairs ─────────────────────────────────────────────────────
        // Unconstrained forms match anywhere; constrained forms need the
        // opening delimiter at a word boundary and the closing one not
        // followed by a word character.
        (new Regex(@"(?<esc>\*\*.+?\*\*)", Opts), Neutralisation.Backslash),
        (new Regex(@"(?<esc>__.+?__)", Opts), Neutralisation.Backslash),
        (new Regex(@"(?<esc>``.+?``)", Opts), Neutralisation.Backslash),
        (new Regex(@"(?<esc>##.+?##)", Opts), Neutralisation.Backslash),
        (new Regex(@"(?:^|(?<=[^\w;:}\\]))(?<esc>\*\S(?:[^\n]*?\S)?\*)(?!\w)", Opts), Neutralisation.Backslash),
        (new Regex(@"(?:^|(?<=[^\w;:}\\]))(?<esc>_\S(?:[^\n]*?\S)?_)(?!\w)", Opts), Neutralisation.Backslash),
        (new Regex(@"(?:^|(?<=[^\w;:}\\]))(?<esc>`\S(?:[^\n]*?\S)?`)(?!\w)", Opts), Neutralisation.Backslash),
        (new Regex(@"(?:^|(?<=[^\w;:}\\]))(?<esc>#\S(?:[^\n]*?\S)?#)(?!\w)", Opts), Neutralisation.Backslash),
        (new Regex(@"(?:^|(?<=[^\w;:}\\]))(?<esc>\+\S(?:[^\n]*?\S)?\+)(?!\w)", Opts), Neutralisation.Backslash),
        (new Regex(@"(?<esc>\^\S+?\^)", Opts), Neutralisation.Backslash),
        (new Regex(@"(?<esc>~\S+?~)", Opts), Neutralisation.Backslash),

        // ── References and anchors ──────────────────────────────────────────
        (new Regex(@"(?<esc>\{[A-Za-z0-9_][A-Za-z0-9_-]*\})", Opts), Neutralisation.Backslash),
        (new Regex(@"(?<esc><<[^<>\n]+>>)", Opts), Neutralisation.Backslash),
        (new Regex(@"(?<esc>\[\[[^\[\]\n]+\]\])", Opts), Neutralisation.Backslash),
        (new Regex(@"(?<esc><\d+>)\s*$", Opts), Neutralisation.Backslash),
        (new Regex(@"(?:^|(?<=[\s(\[<]))(?<esc>(?:https?|ftp|irc):\/\/[^\s\[\]<>]+)", Opts), Neutralisation.Backslash),

        // ── Macro-shaped text ───────────────────────────────────────────────
        // The whole macro is neutralised, not just its `name:` prefix: a
        // prefix-only passthrough leaves a bare URL behind that would then
        // auto-link on its own.
        (new Regex(@"(?:^|(?<=\W))(?<esc>(?:link|image|footnote|footnoteref|xref|kbd|btn|menu|icon|indexterm|stem|latexmath|asciimath|pass|mailto|include|video|audio):{1,2}[^\s\[]*\[[^\]\n]*\])", Opts),
            Neutralisation.Passthrough),

        // ── Text replacements ───────────────────────────────────────────────
        (new Regex(@"(?<esc>\((?:C|R|TM)\))", Opts), Neutralisation.Passthrough),
        (new Regex(@"(?<esc>\.{3,})", Opts), Neutralisation.Passthrough),
        (new Regex(@"(?<esc>->|=>|<-|<=)", Opts), Neutralisation.Passthrough),
        (new Regex(@"(?<=\w)(?<esc>-{2,})(?=\w)", Opts), Neutralisation.Passthrough),
        (new Regex(@"(?:^|(?<=\s))(?<esc>-{2,})(?=\s|$)", Opts), Neutralisation.Passthrough),
        (new Regex(@"(?<esc>-{4,})", Opts), Neutralisation.Passthrough),
        (new Regex(@"(?<=\w)(?<esc>')(?=\w)", Opts), Neutralisation.Passthrough),
    };

    // Markers that would turn the text of a block into a different block if
    // they opened its line. The "esc" group is the marker itself; the rest of
    // the line is left alone.
    private static readonly Regex[] BlockStartPatterns =
    {
        new(@"^(?<esc>=+)(?= )", Opts | RegexOptions.Multiline),
        new(@"^(?<esc>\*+)(?= )", Opts | RegexOptions.Multiline),
        new(@"^(?<esc>-+)(?= )", Opts | RegexOptions.Multiline),
        new(@"^(?<esc>\.+)(?= )", Opts | RegexOptions.Multiline),
        new(@"^(?<esc>\d+\.)(?= )", Opts | RegexOptions.Multiline),
        new(@"^(?<esc>[a-zA-Z]\.)(?= )", Opts | RegexOptions.Multiline),
        new(@"^(?<esc>[ivxIVX]+\))(?= )", Opts | RegexOptions.Multiline),
        new(@"^(?<esc>//)", Opts | RegexOptions.Multiline),
        new(@"^(?<esc>\[)", Opts | RegexOptions.Multiline),
        new(@"^(?<esc>:[^:\s]+:)", Opts | RegexOptions.Multiline),
        new(@"^(?<esc>\|=*)", Opts | RegexOptions.Multiline),
        new(@"^(?<esc>\+)$", Opts | RegexOptions.Multiline),
        new(@"^(?<esc><<<)", Opts | RegexOptions.Multiline),
        new(@"^(?<esc>''')", Opts | RegexOptions.Multiline),
        new(@"^(?<esc>[.\-=_*/]{4,})$", Opts | RegexOptions.Multiline),
        new(@"^\S+(?<esc>::)(?=\s|$)", Opts | RegexOptions.Multiline),
    };

    /// <summary>
    /// Neutralises inline text. <paramref name="insideTableCell"/> also
    /// escapes the cell separator.
    /// </summary>
    public static string EscapeInline(string text, bool insideTableCell = false)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var escaped = Apply(text, InlinePatterns);
        if (insideTableCell) escaped = escaped.Replace("|", "\\|");
        return escaped;
    }

    /// <summary>
    /// Neutralises block markers at the start of any line of
    /// <paramref name="text"/>. Applied to the leading text of a block after
    /// <see cref="EscapeInline"/>, so text already neutralised inline is not
    /// touched twice.
    /// </summary>
    public static string EscapeBlockStart(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var patterns = new (Regex, Neutralisation)[BlockStartPatterns.Length];
        for (var i = 0; i < BlockStartPatterns.Length; i++)
            patterns[i] = (BlockStartPatterns[i], Neutralisation.Passthrough);

        return Apply(text, patterns);
    }

    private static string Apply(string text, (Regex Pattern, Neutralisation Mode)[] patterns)
    {
        List<(int Start, int End, Neutralisation Mode)>? spans = null;

        foreach (var (pattern, mode) in patterns)
        {
            foreach (Match match in pattern.Matches(text))
            {
                var group = match.Groups["esc"];
                if (!group.Success || group.Length == 0) continue;
                (spans ??= new List<(int, int, Neutralisation)>()).Add((group.Index, group.Index + group.Length, mode));
            }
        }

        if (spans is null) return text;

        // Outermost-earliest wins: neutralising the opening of the enclosing
        // construct already covers everything nested inside it, and a second
        // escape in the middle would show up in the output.
        spans.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : b.End.CompareTo(a.End));

        var sb = new StringBuilder(text.Length + 8);
        var position = 0;
        foreach (var span in spans)
        {
            if (span.Start < position) continue;

            sb.Append(text, position, span.Start - position);
            var payload = text.Substring(span.Start, span.End - span.Start);
            sb.Append(span.Mode == Neutralisation.Backslash ? "\\" + payload : Passthrough(payload));
            position = span.End;
        }

        sb.Append(text, position, text.Length - position);
        return sb.ToString();
    }

    /// <summary>
    /// Wraps <paramref name="payload"/> in an inline passthrough. Because a
    /// passthrough also suppresses special-character encoding, any
    /// <c>&lt;</c>, <c>&gt;</c> or <c>&amp;</c> is left outside it — wrapping
    /// only the tail is enough to stop the surrounding pattern from matching.
    /// </summary>
    private static string Passthrough(string payload)
    {
        // Wrapping any one run of non-special characters is enough to stop the
        // surrounding pattern from matching, and it keeps <, > and & outside
        // the passthrough where the special-character substitution still sees
        // them. Take the first such run.
        var start = -1;
        for (var i = 0; i < payload.Length; i++)
        {
            if (IsSpecial(payload[i])) continue;
            start = i;
            break;
        }

        // A span made entirely of special characters cannot be split; the
        // backslash escape is the only remaining option and works for the
        // constructs that reach this path.
        if (start < 0) return "\\" + payload;

        var end = start;
        while (end < payload.Length && !IsSpecial(payload[end])) end++;

        return payload.Substring(0, start)
               + PassOpen + payload.Substring(start, end - start) + PassClose
               + payload.Substring(end);
    }

    private static bool IsSpecial(char c) => c == '<' || c == '>' || c == '&';

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
