using System.Text;
using AdocNet.Ast;

namespace AdocNet.Converters.Man;

public sealed partial class ManRenderer
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
                sb.Append(EscapeBodyText(n.Value));
                break;

            case StrongInlineNode n:
                sb.Append("\\fB");
                RenderInlines(sb, n.Children);
                sb.Append("\\fR");
                break;

            case EmphasisInlineNode n:
                sb.Append("\\fI");
                RenderInlines(sb, n.Children);
                sb.Append("\\fR");
                break;

            case MonospaceInlineNode n:
                sb.Append("\\fB");
                RenderInlines(sb, n.Children);
                sb.Append("\\fR");
                break;

            case LinkInlineNode n:
                sb.Append("\\fI");
                sb.Append(EscapeBodyText(n.Url));
                sb.Append("\\fR");
                break;

            case InlineLinkMacroNode n:
                if (n.Label.Length > 0)
                {
                    sb.Append(EscapeBodyText(n.Label));
                    sb.Append(" (\\fI");
                    sb.Append(EscapeBodyText(n.Url));
                    sb.Append("\\fR)");
                }
                else
                {
                    sb.Append("\\fI");
                    sb.Append(EscapeBodyText(n.Url));
                    sb.Append("\\fR");
                }
                break;

            case CrossReferenceInlineNode n:
                sb.Append("\\fI");
                sb.Append(EscapeBodyText(n.Label ?? n.Target));
                sb.Append("\\fR");
                break;

            case InterDocumentXrefNode n:
                sb.Append("\\fI");
                sb.Append(EscapeBodyText(n.Label ?? n.Path));
                sb.Append("\\fR");
                break;

            case SuperscriptInlineNode n:
                sb.Append('^');
                sb.Append(EscapeBodyText(n.Content));
                break;

            case SubscriptInlineNode n:
                sb.Append('_');
                sb.Append(EscapeBodyText(n.Content));
                break;

            case PassthroughInlineNode n:
                sb.Append(n.Content);
                break;

            case HighlightInlineNode n:
                RenderInlines(sb, n.Children);
                break;

            case FootnoteInlineNode n:
                sb.Append("[footnote]");
                break;

            case InlineImageNode n:
                sb.Append("[Image: ");
                sb.Append(EscapeBodyText(n.Alt.Length > 0 ? n.Alt : n.Target));
                sb.Append(']');
                break;

            case InlineAnchorNode:
                break; // Anchors have no visible representation in man pages

            case InlineMacroNode n:
                RenderInlineMacro(sb, n);
                break;

            case StemInlineNode n:
                sb.Append(EscapeBodyText(n.Content));
                break;

            case IndexTermNode n:
                if (n.Terms.Count > 0)
                    sb.Append(EscapeBodyText(n.Terms[0]));
                break;

            case IndexTermHiddenNode:
                break; // Hidden index terms have no output

            default:
                break;
        }
    }

    private static void RenderInlineMacro(StringBuilder sb, InlineMacroNode macro)
    {
        switch (macro.Name)
        {
            case "kbd":
                sb.Append("\\fB");
                sb.Append(EscapeBodyText(macro.Content));
                sb.Append("\\fR");
                break;
            case "btn":
                sb.Append("[\\fB");
                sb.Append(EscapeBodyText(macro.Content));
                sb.Append("\\fR]");
                break;
            case "menu":
                sb.Append(EscapeBodyText(macro.Target));
                sb.Append(" > ");
                sb.Append(EscapeBodyText(macro.Content));
                break;
            default:
                sb.Append(EscapeBodyText(macro.Content));
                break;
        }
    }

    // ── Utilities ───────────────────────────────────────────────────────

    private static string GetInlinesPlainText(IReadOnlyList<InlineNode> inlines)
    {
        if (inlines.Count == 1 && inlines[0] is TextInlineNode t)
            return t.Value;

        var sb = new StringBuilder();
        foreach (var inline in inlines)
            AppendPlainText(sb, inline);
        return sb.ToString();
    }

    private static void AppendPlainText(StringBuilder sb, InlineNode node)
    {
        switch (node)
        {
            case TextInlineNode n: sb.Append(n.Value); break;
            case StrongInlineNode n:
                foreach (var c in n.Children) AppendPlainText(sb, c);
                break;
            case EmphasisInlineNode n:
                foreach (var c in n.Children) AppendPlainText(sb, c);
                break;
            case MonospaceInlineNode n:
                foreach (var c in n.Children) AppendPlainText(sb, c);
                break;
            default: break;
        }
    }
}
