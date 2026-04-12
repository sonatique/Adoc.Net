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
                EscapeTo(sb, n.Label.Length > 0 ? n.Label : n.Url);
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
