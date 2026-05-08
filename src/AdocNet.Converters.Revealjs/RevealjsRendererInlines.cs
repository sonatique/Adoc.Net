using System.Text;
using AdocNet.Ast;

namespace AdocNet.Converters.Revealjs;

public sealed partial class RevealjsRenderer
{
    // ── Inline rendering ────────────────────────────────────────────────

    private static void RenderInlines(
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
    private static void RenderTextAsInlines(StringBuilder sb, string text)
    {
        var subs = SubstitutionKind.Quotes |
                   SubstitutionKind.Replacements |
                   SubstitutionKind.PostReplacements;
        var inlines = AdocNet.Parser.InlineParser.Parse(text, subs, EmptyAttrs);
        RenderInlines(sb, inlines);
    }

    private static readonly Dictionary<string, string> EmptyAttrs = new();

    /// <summary>
    /// Renders a section's title using its pre-parsed TitleInlines when available,
    /// falling back to parsing the raw Title string. The parser already populates
    /// TitleInlines for section nodes, so this avoids redundant work.
    /// </summary>
    private static void RenderSectionTitle(StringBuilder sb, SectionNode section)
    {
        if (section.TitleInlines is { Count: > 0 })
            RenderInlines(sb, section.TitleInlines);
        else
            RenderTextAsInlines(sb, section.Title);
    }

    private static void RenderInline(StringBuilder sb, InlineNode node)
    {
        switch (node)
        {
            case TextInlineNode n:
                EscapeTo(sb, n.Value);
                break;
            case StrongInlineNode n:
                sb.Append("<strong>");
                RenderInlines(sb, n.Children);
                sb.Append("</strong>");
                break;
            case EmphasisInlineNode n:
                sb.Append("<em>");
                RenderInlines(sb, n.Children);
                sb.Append("</em>");
                break;
            case MonospaceInlineNode n:
                sb.Append("<code>");
                RenderInlines(sb, n.Children);
                sb.Append("</code>");
                break;
            case LinkInlineNode n:
                sb.Append("<a href=\"");
                EscapeTo(sb, n.Url);
                sb.Append("\">");
                EscapeTo(sb, n.Url);
                sb.Append("</a>");
                break;
            case InlineLinkMacroNode n:
                sb.Append("<a href=\"");
                EscapeTo(sb, n.Url);
                sb.Append("\">");
                if (n.Label.Length > 0)
                    RenderTextAsInlines(sb, n.Label);
                else
                    EscapeTo(sb, n.Url);
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
