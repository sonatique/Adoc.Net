using System.Text;
using AdocNet.Ast;

namespace AdocNet.Converters.Revealjs;

public sealed partial class RevealjsRenderer
{
    // ── Inline rendering ────────────────────────────────────────────────

    private void RenderInlines(
        StringBuilder sb, IEnumerable<InlineNode> inlines)
    {
        foreach (var node in inlines)
            RenderInline(sb, node);
    }

    /// <summary>
    /// Parses a title or label string with formatting substitutions and renders
    /// the resulting inlines so backticks become &lt;code&gt;, *text* becomes
    /// &lt;strong&gt;, etc. Macros are excluded to avoid re-entering link parsing.
    /// </summary>
    private void RenderTextAsInlines(StringBuilder sb, string text)
    {
        var subs = SubstitutionKind.Quotes |
                   SubstitutionKind.Replacements |
                   SubstitutionKind.PostReplacements;
        var inlines = AdocNet.Parser.InlineParser.Parse(text, subs, EmptyAttrs);
        RenderInlines(sb, inlines);
    }

    private static readonly Dictionary<string, string> EmptyAttrs = new();

    /// <summary>
    /// Strips http://, https://, mailto: prefixes from the displayed URL when
    /// :hide-uri-scheme: is set on the document. The href stays untouched —
    /// only the user-visible text is shortened (Asciidoctor parity).
    /// </summary>
    private string MaybeHideUriScheme(string url)
    {
        if (!_hideUriScheme) return url;
        foreach (var prefix in new[] { "https://", "http://", "ftp://", "mailto:", "irc://" })
        {
            if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return url.Substring(prefix.Length);
        }
        return url;
    }

    /// <summary>
    /// Opens an inline element tag, appending a class attribute when roles are
    /// present. Used by strong/emphasis/monospace to propagate [.role]…[/role]
    /// span roles (e.g. [.term]*target* → &lt;strong class="term"&gt;).
    /// </summary>
    private static void AppendInlineWithRoles(StringBuilder sb, string tagName, IReadOnlyList<string>? roles)
    {
        sb.Append('<').Append(tagName);
        if (roles is { Count: > 0 })
        {
            sb.Append(" class=\"");
            for (int i = 0; i < roles.Count; i++)
            {
                if (i > 0) sb.Append(' ');
                EscapeTo(sb, roles[i]);
            }
            sb.Append('"');
        }
        sb.Append('>');
    }

    /// <summary>
    /// Renders the supported subset of inline macros (kbd, btn, menu) to match
    /// Asciidoctor's reveal.js output. Unrecognised macros fall through to a
    /// literal-text rendering of their target/content.
    /// </summary>
    private void RenderInlineMacro(StringBuilder sb, InlineMacroNode macro)
    {
        switch (macro.Name)
        {
            case "kbd":
                {
                    var keys = macro.Content.Split('+');
                    if (keys.Length == 1)
                    {
                        sb.Append("<kbd>");
                        EscapeTo(sb, keys[0].Trim());
                        sb.Append("</kbd>");
                    }
                    else
                    {
                        sb.Append("<span class=\"keyseq\">");
                        for (int k = 0; k < keys.Length; k++)
                        {
                            if (k > 0) sb.Append('+');
                            sb.Append("<kbd>");
                            EscapeTo(sb, keys[k].Trim());
                            sb.Append("</kbd>");
                        }
                        sb.Append("</span>");
                    }
                }
                break;
            case "btn":
                sb.Append("<b class=\"button\">");
                EscapeTo(sb, macro.Content);
                sb.Append("</b>");
                break;
            case "menu":
                sb.Append("<span class=\"menuseq\"><span class=\"menu\">");
                EscapeTo(sb, macro.Target);
                sb.Append("</span>&#160;&#9656; <span class=\"submenu\">");
                EscapeTo(sb, macro.Content);
                sb.Append("</span></span>");
                break;
            default:
                // Unknown macro: render as plain text "name:target[content]".
                EscapeTo(sb, macro.Name);
                sb.Append(':');
                EscapeTo(sb, macro.Target);
                sb.Append('[');
                EscapeTo(sb, macro.Content);
                sb.Append(']');
                break;
        }
    }

    /// <summary>
    /// Renders a section's title using its pre-parsed TitleInlines when available,
    /// falling back to parsing the raw Title string. When :sectnums: is enabled,
    /// slide-level sections (level 1 horizontal slides and level 2 vertical
    /// slides) get a numeric prefix; deeper headings rendered inside a slide
    /// (level 3+) are not numbered, matching Asciidoctor's reveal.js convention.
    /// </summary>
    private void RenderSectionTitle(StringBuilder sb, SectionNode section)
    {
        // Appendix sections get an "Appendix A: ", "Appendix B: " prefix
        // (Asciidoctor parity). The appendix counter is global to the document.
        if (string.Equals(section.Style, "appendix", StringComparison.OrdinalIgnoreCase))
        {
            char letter = (char)('A' + _appendixCounter++);
            sb.Append("Appendix ");
            sb.Append(letter);
            sb.Append(": ");
        }
        else if (section.Level <= 2)
        {
            var prefix = AdvanceSectionNumber(section.Level);
            if (prefix is not null)
                sb.Append(prefix);
        }
        if (section.TitleInlines is { Count: > 0 })
            RenderInlines(sb, section.TitleInlines);
        else
            RenderTextAsInlines(sb, section.Title);
    }

    private void RenderInline(StringBuilder sb, InlineNode node)
    {
        switch (node)
        {
            case TextInlineNode n:
                EscapeTo(sb, n.Value);
                break;
            case StrongInlineNode n:
                AppendInlineWithRoles(sb, "strong", n.Roles);
                RenderInlines(sb, n.Children);
                sb.Append("</strong>");
                break;
            case EmphasisInlineNode n:
                AppendInlineWithRoles(sb, "em", n.Roles);
                RenderInlines(sb, n.Children);
                sb.Append("</em>");
                break;
            case MonospaceInlineNode n:
                AppendInlineWithRoles(sb, "code", n.Roles);
                RenderInlines(sb, n.Children);
                sb.Append("</code>");
                break;
            case LinkInlineNode n:
                // Auto-detected URLs (no explicit label) get class="bare" matching
                // Asciidoctor's convention. The href keeps the full URL; the
                // displayed text strips the scheme when :hide-uri-scheme: is set.
                sb.Append("<a class=\"bare\" href=\"");
                EscapeTo(sb, n.Url);
                sb.Append("\">");
                EscapeTo(sb, MaybeHideUriScheme(n.Url));
                sb.Append("</a>");
                break;
            case InlineLinkMacroNode n:
                bool isBare = n.Label.Length == 0 || n.Label == n.Url;
                sb.Append("<a");
                if (isBare)
                    sb.Append(" class=\"bare\"");
                sb.Append(" href=\"");
                EscapeTo(sb, n.Url);
                sb.Append('"');
                // Window (link target — '_blank' from '^' suffix or window= attr)
                // becomes target="…" on <a>. Asciidoctor adds rel="noopener" only
                // when window=_blank, but reveal.js converter omits rel for parity.
                if (n.Window is not null)
                {
                    sb.Append(" target=\"");
                    EscapeTo(sb, n.Window);
                    sb.Append('"');
                }
                sb.Append('>');
                if (n.Label.Length > 0)
                    RenderTextAsInlines(sb, n.Label);
                else
                    EscapeTo(sb, MaybeHideUriScheme(n.Url));
                sb.Append("</a>");
                break;
            case HighlightInlineNode n:
                sb.Append("<mark>");
                RenderInlines(sb, n.Children);
                sb.Append("</mark>");
                break;
            case SuperscriptInlineNode n:
                sb.Append("<sup>");
                EscapeTo(sb, n.Content);
                sb.Append("</sup>");
                break;
            case SubscriptInlineNode n:
                sb.Append("<sub>");
                EscapeTo(sb, n.Content);
                sb.Append("</sub>");
                break;
            case PassthroughInlineNode n:
                sb.Append(n.Content);
                break;
            case StemInlineNode n:
                if (string.Equals(n.StemType, "asciimath", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append("\\$");
                    sb.Append(n.Content);
                    sb.Append("\\$");
                }
                else
                {
                    sb.Append("\\(");
                    sb.Append(n.Content);
                    sb.Append("\\)");
                }
                break;
            case CrossReferenceInlineNode n:
                sb.Append("<a href=\"#");
                EscapeTo(sb, n.Target);
                sb.Append("\">");
                if (n.Label is not null)
                    RenderTextAsInlines(sb, n.Label);
                else
                {
                    sb.Append('[');
                    EscapeTo(sb, n.Target);
                    sb.Append(']');
                }
                sb.Append("</a>");
                break;
            case InterDocumentXrefNode n:
                {
                    // .adoc -> .html; append #id when present.
                    var href = n.Path.EndsWith(".adoc", StringComparison.Ordinal)
                        ? n.Path.Substring(0, n.Path.Length - 5) + ".html"
                        : n.Path;
                    if (n.Id is not null)
                        href += "#" + n.Id;
                    sb.Append("<a href=\"");
                    EscapeTo(sb, href);
                    sb.Append("\">");
                    if (n.Label is not null)
                    {
                        RenderTextAsInlines(sb, n.Label);
                    }
                    else
                    {
                        // Asciidoctor's no-label fallback: [basename] (no extension,
                        // wrapped in brackets) — not the converted .html path.
                        var basename = n.Path;
                        var lastSlash = basename.LastIndexOfAny(new[] { '/', '\\' });
                        if (lastSlash >= 0) basename = basename.Substring(lastSlash + 1);
                        var dot = basename.LastIndexOf('.');
                        if (dot > 0) basename = basename.Substring(0, dot);
                        sb.Append('[');
                        EscapeTo(sb, basename);
                        sb.Append(']');
                    }
                    sb.Append("</a>");
                    break;
                }
            case InlineImageNode n:
                sb.Append("<span class=\"image\"><img src=\"");
                EscapeTo(sb, n.Target);
                sb.Append("\" alt=\"");
                EscapeTo(sb, n.Alt);
                sb.Append("\"></span>");
                break;
            case InlineMacroNode macro:
                RenderInlineMacro(sb, macro);
                break;
            case FootnoteInlineNode n:
                // Asciidoctor reveal.js inline marker:
                //   <sup class="footnote">[<span class="footnote" title="View footnote.">N</span>]</sup>
                // The footnote text is buffered into the per-slide list and
                // emitted as a numbered <div class="footnote"> by EndSlideFootnotes.
                {
                    // Resolve footnote text (rendered inline children → string).
                    string footnoteText;
                    if (n.Inlines.Count > 0)
                    {
                        var inner = new StringBuilder();
                        RenderInlines(inner, n.Inlines);
                        footnoteText = inner.ToString();
                    }
                    else
                    {
                        footnoteText = n.Text ?? "";
                    }
                    _slideFootnoteTexts.Add(footnoteText);
                    int num = _slideFootnoteTexts.Count;
                    sb.Append("<sup class=\"footnote\">[<span class=\"footnote\" title=\"View footnote.\">");
                    sb.Append(num);
                    sb.Append("</span>]</sup>");
                }
                break;
            default:
                break;
        }
    }

    // ── Utilities ───────────────────────────────────────────────────────

    private static string GetAttribute(DocumentNode doc, string name, string defaultValue)
    {
        return doc.Attributes.TryGetValue(name, out var val) && val.Length > 0
            ? val : defaultValue;
    }

    private static void EscapeTo(StringBuilder sb, string value)
    {
        int segmentStart = 0;
        for (int i = 0; i < value.Length; i++)
        {
            string? entity = value[i] switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                '"' => "&quot;",
                '\'' => "&#39;",
                _ => null,
            };

            if (entity is not null)
            {
                if (i > segmentStart)
                    sb.Append(value, segmentStart, i - segmentStart);
                sb.Append(entity);
                segmentStart = i + 1;
            }
        }

        if (segmentStart == 0)
            sb.Append(value);
        else if (segmentStart < value.Length)
            sb.Append(value, segmentStart, value.Length - segmentStart);
    }
}
