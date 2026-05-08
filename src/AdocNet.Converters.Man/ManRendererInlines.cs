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

    /// <summary>
    /// Parses a title or label string with formatting substitutions and renders
    /// the resulting inlines. Backticks become bold, *text* becomes bold, etc.
    /// Macros are excluded to avoid re-entry into link parsing.
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
                sb.Append("\\fP");
                break;

            case EmphasisInlineNode n:
                sb.Append("\\fI");
                RenderInlines(sb, n.Children);
                sb.Append("\\fP");
                break;

            case MonospaceInlineNode n:
                sb.Append("\\fB");
                RenderInlines(sb, n.Children);
                sb.Append("\\fP");
                break;

            case LinkInlineNode n:
                sb.Append("\\fI");
                sb.Append(EscapeBodyText(n.Url));
                sb.Append("\\fP");
                break;

            case InlineLinkMacroNode n:
                if (n.Label.Length > 0)
                {
                    RenderTextAsInlines(sb, n.Label);
                    sb.Append(" (\\fI");
                    sb.Append(EscapeBodyText(n.Url));
                    sb.Append("\\fP)");
                }
                else
                {
                    sb.Append("\\fI");
                    sb.Append(EscapeBodyText(n.Url));
                    sb.Append("\\fP");
                }
                break;

            case CrossReferenceInlineNode n:
                sb.Append("\\fI");
                if (n.Label is not null)
                    RenderTextAsInlines(sb, n.Label);
                else
                    sb.Append(EscapeBodyText(n.Target));
                sb.Append("\\fP");
                break;

            case InterDocumentXrefNode n:
                sb.Append("\\fI");
                if (n.Label is not null)
                    RenderTextAsInlines(sb, n.Label);
                else
                    sb.Append(EscapeBodyText(n.Path));
                sb.Append("\\fP");
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
                sb.Append("\\fP");
                break;
            case "btn":
                sb.Append("[\\fB");
                sb.Append(EscapeBodyText(macro.Content));
                sb.Append("\\fP]");
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
