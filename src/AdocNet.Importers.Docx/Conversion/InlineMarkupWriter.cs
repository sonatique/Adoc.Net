using System.Collections.Generic;
using System.Text;
using AdocNet.Ast;

namespace AdocNet.Importers.Docx;

/// <summary>
/// Serialises inline nodes to AsciiDoc markup. The emitter in
/// <c>AdocNet.Emitter</c> only exposes block-level entry points, but the
/// importer needs inline markup as a <em>string</em> in two places: link macro
/// labels and AsciiDoc-styled table cells. This mirrors the emitter's inline
/// forms for the node kinds the importer produces.
/// </summary>
internal static class InlineMarkupWriter
{
    public static string Write(IReadOnlyList<InlineNode> nodes)
    {
        var sb = new StringBuilder();
        foreach (var node in nodes) Write(node, sb);
        return sb.ToString();
    }

    private static void Write(InlineNode node, StringBuilder sb)
    {
        switch (node)
        {
            case TextInlineNode text:
                sb.Append(text.Value);
                break;

            case StrongInlineNode strong:
                WriteRoles(strong.Roles, sb);
                sb.Append('*');
                WriteAll(strong.Children, sb);
                sb.Append('*');
                break;

            case EmphasisInlineNode em:
                WriteRoles(em.Roles, sb);
                sb.Append('_');
                WriteAll(em.Children, sb);
                sb.Append('_');
                break;

            case MonospaceInlineNode mono:
                WriteRoles(mono.Roles, sb);
                sb.Append('`');
                WriteAll(mono.Children, sb);
                sb.Append('`');
                break;

            case HighlightInlineNode highlight:
                WriteRoles(highlight.Roles, sb);
                sb.Append('#');
                WriteAll(highlight.Children, sb);
                sb.Append('#');
                break;

            case SuperscriptInlineNode sup:
                sb.Append('^').Append(sup.Content).Append('^');
                break;

            case SubscriptInlineNode sub:
                sb.Append('~').Append(sub.Content).Append('~');
                break;

            case InlineLinkMacroNode link:
                sb.Append("link:").Append(link.Url).Append('[').Append(link.Label).Append(']');
                break;

            case LinkInlineNode bare:
                sb.Append(bare.Url);
                break;

            case InlineImageNode image:
                sb.Append("image:").Append(image.Target).Append('[').Append(image.Alt);
                if (!string.IsNullOrEmpty(image.Width)) sb.Append(',').Append(image.Width);
                if (!string.IsNullOrEmpty(image.Height)) sb.Append(',').Append(image.Height);
                sb.Append(']');
                break;

            case CrossReferenceInlineNode xref:
                sb.Append("<<").Append(xref.Target);
                if (xref.Label is not null) sb.Append(',').Append(xref.Label);
                sb.Append(">>");
                break;

            case FootnoteInlineNode footnote:
                sb.Append("footnote:");
                if (footnote.Id is not null) sb.Append(footnote.Id);
                sb.Append('[');
                if (footnote.Inlines.Count > 0) WriteAll(footnote.Inlines, sb);
                else if (footnote.Text is not null) sb.Append(footnote.Text);
                sb.Append(']');
                break;

            case InlineAnchorNode anchor:
                sb.Append("[[").Append(anchor.Id);
                if (anchor.Reftext is not null) sb.Append(',').Append(anchor.Reftext);
                sb.Append("]]");
                break;
        }
    }

    private static void WriteAll(IReadOnlyList<InlineNode> nodes, StringBuilder sb)
    {
        foreach (var node in nodes) Write(node, sb);
    }

    private static void WriteRoles(IReadOnlyList<string>? roles, StringBuilder sb)
    {
        if (roles is null || roles.Count == 0) return;
        sb.Append('[');
        foreach (var role in roles) sb.Append('.').Append(role);
        sb.Append(']');
    }

    /// <summary>
    /// Markup for a formatted span using AsciiDoc's <em>unconstrained</em>
    /// delimiters (<c>**bold**</c>, <c>__italic__</c>, <c>``mono``</c>,
    /// <c>##mark##</c>). Constrained delimiters only work at word boundaries,
    /// and Word happily formats half a word — <c>IBA</c> highlighted followed
    /// by a plain <c>N</c> would otherwise emit <c>#IBA#N</c>, which does not
    /// parse as a span at all.
    /// </summary>
    public static string WriteUnconstrained(InlineNode node)
    {
        var sb = new StringBuilder();
        switch (node)
        {
            case StrongInlineNode strong:
                WriteRoles(strong.Roles, sb);
                sb.Append("**");
                WriteAll(strong.Children, sb);
                sb.Append("**");
                break;

            case EmphasisInlineNode em:
                WriteRoles(em.Roles, sb);
                sb.Append("__");
                WriteAll(em.Children, sb);
                sb.Append("__");
                break;

            case MonospaceInlineNode mono:
                WriteRoles(mono.Roles, sb);
                sb.Append("``");
                WriteAll(mono.Children, sb);
                sb.Append("``");
                break;

            case HighlightInlineNode highlight:
                WriteRoles(highlight.Roles, sb);
                sb.Append("##");
                WriteAll(highlight.Children, sb);
                sb.Append("##");
                break;

            default:
                Write(node, sb);
                break;
        }

        return sb.ToString();
    }

    /// <summary>
    /// Markup for a link macro's label. Role prefixes are dropped and <c>]</c>
    /// is escaped, because the label lives inside the macro's attribute list
    /// where a bracket would end it early.
    /// </summary>
    public static string WriteLinkLabel(IReadOnlyList<InlineNode> nodes)
    {
        var sb = new StringBuilder();
        foreach (var node in nodes) WriteWithoutRoles(node, sb);
        return sb.ToString().Replace("]", "\\]");
    }

    private static void WriteWithoutRoles(InlineNode node, StringBuilder sb)
    {
        switch (node)
        {
            case StrongInlineNode strong:
                sb.Append('*');
                foreach (var child in strong.Children) WriteWithoutRoles(child, sb);
                sb.Append('*');
                break;

            case EmphasisInlineNode em:
                sb.Append('_');
                foreach (var child in em.Children) WriteWithoutRoles(child, sb);
                sb.Append('_');
                break;

            case MonospaceInlineNode mono:
                sb.Append('`');
                foreach (var child in mono.Children) WriteWithoutRoles(child, sb);
                sb.Append('`');
                break;

            case HighlightInlineNode highlight:
                // A highlight span that only exists to carry roles is dropped;
                // one that came from a real Word highlight keeps its marks.
                if (highlight.Roles is { Count: > 0 })
                {
                    foreach (var child in highlight.Children) WriteWithoutRoles(child, sb);
                    break;
                }

                sb.Append('#');
                foreach (var child in highlight.Children) WriteWithoutRoles(child, sb);
                sb.Append('#');
                break;

            default:
                Write(node, sb);
                break;
        }
    }

    /// <summary>Plain text of an inline tree, with all markup removed.</summary>
    public static string PlainText(IReadOnlyList<InlineNode> nodes)
    {
        var sb = new StringBuilder();
        PlainText(nodes, sb);
        return sb.ToString();
    }

    private static void PlainText(IReadOnlyList<InlineNode> nodes, StringBuilder sb)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case TextInlineNode text: sb.Append(text.Value); break;
                case StrongInlineNode s: PlainText(s.Children, sb); break;
                case EmphasisInlineNode e: PlainText(e.Children, sb); break;
                case MonospaceInlineNode m: PlainText(m.Children, sb); break;
                case HighlightInlineNode h: PlainText(h.Children, sb); break;
                case SuperscriptInlineNode sup: sb.Append(sup.Content); break;
                case SubscriptInlineNode sub: sb.Append(sub.Content); break;
                case InlineLinkMacroNode link: sb.Append(link.Label); break;
                case LinkInlineNode bare: sb.Append(bare.Url); break;
                case CrossReferenceInlineNode xref: sb.Append(xref.Label ?? xref.Target); break;
            }
        }
    }
}
