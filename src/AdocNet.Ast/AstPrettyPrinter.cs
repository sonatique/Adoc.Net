using System.Text;
using AdocNet.Internal.Compatibility;

namespace AdocNet.Ast;

/// <summary>
/// Renders an AST tree to indented text for debugging and test assertions.
/// </summary>
public static class AstPrettyPrinter
{
    public static string Print(AstNode node, bool includeSourceRanges = true)
    {
        Guard.NotNull(node);
        var sb = new StringBuilder();
        PrintNode(sb, node, indent: 0, includeSourceRanges);
        return sb.ToString();
    }

    private static void PrintNode(StringBuilder sb, AstNode node, int indent, bool includeSourceRanges)
    {
        var prefix = new string(' ', indent * 2);

        sb.Append(prefix);
        sb.Append(node.Kind.ToString());

        if (includeSourceRanges && !node.Source.IsNone)
            sb.Append($" [{node.Source}]");

        // Show Id and Role for block nodes that have them (before other properties).
        if (node is BlockNode blockNode)
        {
            if (blockNode.Id is not null)
                sb.Append($" Id={Quote(blockNode.Id)}");
            if (blockNode.Roles.Count > 0)
                sb.Append($" Roles={Quote(string.Join(",", blockNode.Roles))}");
        }

        foreach (var prop in node.GetProperties())
            sb.Append($" {prop.Key}={Quote(prop.Value)}");

        sb.Append('\n');

        // Inline collections are printed as children of the nodes that carry them.
        // This covers ParagraphNode.Inlines, SectionNode.TitleInlines, ListItemNode.Inlines,
        // and TableCellNode.Inlines.
        foreach (var inline in GetNodeInlines(node))
            PrintNode(sb, inline, indent + 1, includeSourceRanges);

        foreach (var child in node.Children)
            PrintNode(sb, child, indent + 1, includeSourceRanges);
    }

    /// <summary>
    /// Returns the inline-node collection for nodes that hold one, or an empty enumerable.
    /// Block children (node.Children) are handled separately in PrintNode.
    /// </summary>
    private static IEnumerable<InlineNode> GetNodeInlines(AstNode node)
    {
        if (node is ParagraphNode       p) return p.Inlines;
        if (node is SectionNode         s) return s.TitleInlines;
        if (node is ListItemNode        l) return l.Inlines;
        if (node is TableCellNode       c) return c.Inlines;
        if (node is AdmonitionNode      a) return a.Inlines;
        if (node is DescriptionItemNode d) return [.. d.TermInlines, .. d.DescriptionInlines];
        if (node is StrongInlineNode    st) return st.Children;
        if (node is EmphasisInlineNode  em) return em.Children;
        if (node is MonospaceInlineNode mo) return mo.Children;
        if (node is FootnoteInlineNode fn) return fn.Inlines;
        if (node is BibliographyEntryNode bib) return bib.Inlines;
        return [];
    }

    private static string Quote(string value) =>
        $"\"{value.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
}
