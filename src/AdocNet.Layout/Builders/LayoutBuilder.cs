using System;
using System.Collections.Generic;
using AdocNet.Ast;

namespace AdocNet.Layout.Builders;

/// <summary>
/// Converts an AST <see cref="DocumentNode"/> into a <see cref="DocumentLayout"/> tree.
/// Pure function: no side effects, no state.
/// </summary>
public class LayoutBuilder
{
    /// <summary>
    /// Builds a <see cref="DocumentLayout"/> from the given AST document.
    /// </summary>
    /// <param name="document">The parsed AST document.</param>
    /// <returns>A layout tree suitable for rendering.</returns>
    public DocumentLayout Build(DocumentNode document)
    {
        var blocks = BuildBlocks(document.Children);
        return new DocumentLayout(document.Title, blocks);
    }

    private static IReadOnlyList<BlockLayout> BuildBlocks(IReadOnlyList<AstNode> children)
    {
        if (children.Count == 0)
            return Array.Empty<BlockLayout>();

        var result = new List<BlockLayout>();
        foreach (var child in children)
        {
            BuildBlock(child, result);
        }
        return result;
    }

    private static void BuildBlock(AstNode node, List<BlockLayout> output)
    {
        switch (node)
        {
            case SectionNode section:
                BuildSection(section, output);
                break;
            case ParagraphNode paragraph:
                output.Add(BuildParagraph(paragraph));
                break;
            case ListNode list:
                output.Add(BuildList(list));
                break;
            case DelimitedBlockNode delimited:
                BuildDelimitedBlock(delimited, output);
                break;
            case AdmonitionNode admonition:
                output.Add(BuildAdmonition(admonition));
                break;
            case TableNode table:
                output.Add(BuildTable(table));
                break;
            case DescriptionListNode descList:
                output.Add(BuildDescriptionList(descList));
                break;
            case ThematicBreakNode thematic:
                output.Add(new ThematicBreakLayout { Source = thematic.Source });
                break;
            case TocNode toc:
                BuildToc(toc, output);
                break;
            // Unknown/unsupported nodes: skip silently
        }
    }

    private static void BuildSection(SectionNode section, List<BlockLayout> output)
    {
        var inlines = BuildInlines(section.TitleInlines);
        output.Add(new HeadingLayout(section.Level, inlines) { Source = section.Source });

        foreach (var child in section.Children)
        {
            BuildBlock(child, output);
        }
    }

    private static ParagraphLayout BuildParagraph(ParagraphNode paragraph)
    {
        var inlines = BuildInlines(paragraph.Inlines);
        return new ParagraphLayout(inlines) { Source = paragraph.Source };
    }

    private static ListLayout BuildList(ListNode list)
    {
        bool ordered = list.ListKind == ListKind.Ordered;
        var items = new List<ListItemLayout>();
        foreach (var child in list.Children)
        {
            if (child is ListItemNode item)
            {
                items.Add(BuildListItem(item));
            }
        }
        return new ListLayout(ordered, items) { Source = list.Source };
    }

    private static ListItemLayout BuildListItem(ListItemNode item)
    {
        var inlines = BuildInlines(item.Inlines);
        var blocks = BuildBlocks(item.Children);
        return new ListItemLayout(inlines, blocks);
    }

    private static void BuildDelimitedBlock(DelimitedBlockNode delimited, List<BlockLayout> output)
    {
        switch (delimited.BlockKind)
        {
            case DelimitedBlockKind.Literal:
            case DelimitedBlockKind.Listing:
            case DelimitedBlockKind.Source:
                output.Add(new CodeBlockLayout(delimited.Content ?? string.Empty, delimited.Language)
                {
                    Source = delimited.Source,
                });
                break;
            case DelimitedBlockKind.Example:
            case DelimitedBlockKind.Quote:
            case DelimitedBlockKind.Sidebar:
            case DelimitedBlockKind.Open:
                foreach (var child in delimited.Children)
                {
                    BuildBlock(child, output);
                }
                break;
        }
    }

    private static AdmonitionLayout BuildAdmonition(AdmonitionNode admonition)
    {
        var kind = ParseAdmonitionKind(admonition.AdmonitionType);

        var blocks = new List<BlockLayout>();
        if (admonition.Inlines.Count > 0)
        {
            var inlines = BuildInlines(admonition.Inlines);
            blocks.Add(new ParagraphLayout(inlines) { Source = admonition.Source });
        }
        else
        {
            foreach (var child in admonition.Children)
            {
                BuildBlock(child, blocks);
            }
        }

        return new AdmonitionLayout(kind, blocks) { Source = admonition.Source };
    }

    private static TableLayout BuildTable(TableNode table)
    {
        var rows = new List<TableRowLayout>();
        for (int i = 0; i < table.Children.Count; i++)
        {
            if (table.Children[i] is TableRowNode rowNode)
            {
                bool isHeaderRow = table.HasHeader && i == 0;
                rows.Add(BuildTableRow(rowNode, isHeaderRow));
            }
        }
        return new TableLayout(table.Title, table.HasHeader, table.HasFooter, rows) { Source = table.Source };
    }

    private static TableRowLayout BuildTableRow(TableRowNode rowNode, bool isHeaderRow)
    {
        var cells = new List<TableCellLayout>();
        foreach (var child in rowNode.Children)
        {
            if (child is TableCellNode cellNode)
            {
                var inlines = BuildInlines(cellNode.Inlines);
                if (inlines.Count == 0 && !string.IsNullOrEmpty(cellNode.Text))
                {
                    inlines = new InlineLayout[] { new TextRun(cellNode.Text) };
                }
                bool isHeader = isHeaderRow || cellNode.ContentStyle == TableCellStyle.Header;
                cells.Add(new TableCellLayout(inlines, cellNode.ColSpan, cellNode.RowSpan, isHeader));
            }
        }
        return new TableRowLayout(cells);
    }

    private static DescriptionListLayout BuildDescriptionList(DescriptionListNode descList)
    {
        var items = new List<DescriptionItemLayout>();
        foreach (var child in descList.Children)
        {
            if (child is DescriptionItemNode item)
            {
                var term = BuildInlines(item.TermInlines);
                if (term.Count == 0 && item.Terms.Count > 0 && !string.IsNullOrEmpty(item.Terms[0]))
                    term = new InlineLayout[] { new TextRun(item.Terms[0]) };

                var desc = BuildInlines(item.DescriptionInlines);
                if (desc.Count == 0 && !string.IsNullOrEmpty(item.Description))
                    desc = new InlineLayout[] { new TextRun(item.Description) };

                items.Add(new DescriptionItemLayout(term, desc));
            }
        }
        return new DescriptionListLayout(items) { Source = descList.Source };
    }

    private static void BuildToc(TocNode toc, List<BlockLayout> output)
    {
        if (toc.Entries.Count == 0)
            return;

        var items = new List<ListItemLayout>();
        foreach (var entry in toc.Entries)
        {
            BuildTocEntry(entry, items);
        }
        output.Add(new HeadingLayout(2, new InlineLayout[] { new TextRun("Table of Contents") })
        {
            Source = toc.Source,
        });
        output.Add(new ListLayout(false, items) { Source = toc.Source });
    }

    private static void BuildTocEntry(TocEntry entry, List<ListItemLayout> items)
    {
        var inlines = new InlineLayout[] { new TextRun(entry.Title) };
        var nestedBlocks = new List<BlockLayout>();
        if (entry.Children.Count > 0)
        {
            var childItems = new List<ListItemLayout>();
            foreach (var child in entry.Children)
                BuildTocEntry(child, childItems);
            nestedBlocks.Add(new ListLayout(false, childItems));
        }
        items.Add(new ListItemLayout(inlines, nestedBlocks));
    }

    private static AdmonitionKind ParseAdmonitionKind(string type)
    {
        switch (type.ToUpperInvariant())
        {
            case "NOTE": return AdmonitionKind.Note;
            case "TIP": return AdmonitionKind.Tip;
            case "WARNING": return AdmonitionKind.Warning;
            case "IMPORTANT": return AdmonitionKind.Important;
            case "CAUTION": return AdmonitionKind.Caution;
            default: return AdmonitionKind.Note;
        }
    }

    private static IReadOnlyList<InlineLayout> BuildInlines(IReadOnlyList<InlineNode> nodes)
    {
        if (nodes.Count == 0)
            return Array.Empty<InlineLayout>();

        var result = new List<InlineLayout>();
        foreach (var node in nodes)
        {
            var inline = BuildInline(node);
            if (inline != null)
            {
                result.Add(inline);
            }
        }
        return result;
    }

    private static InlineLayout? BuildInline(InlineNode node)
    {
        switch (node)
        {
            case TextInlineNode text:
                return new TextRun(text.Value);

            case StrongInlineNode strong:
                return new BoldRun(BuildInlines(strong.Children));

            case EmphasisInlineNode emphasis:
                return new ItalicRun(BuildInlines(emphasis.Children));

            case MonospaceInlineNode mono:
                return new MonoRun(BuildInlines(mono.Children));

            case HighlightInlineNode highlight:
                return new BoldRun(BuildInlines(highlight.Children));

            case LinkInlineNode link:
                return new LinkRun(link.Url, new InlineLayout[] { new TextRun(link.Url) });

            case InlineLinkMacroNode linkMacro:
                var macroLabel = string.IsNullOrEmpty(linkMacro.Label) ? linkMacro.Url : linkMacro.Label;
                return new LinkRun(linkMacro.Url, new InlineLayout[] { new TextRun(macroLabel) });

            case CrossReferenceInlineNode xref:
                return new TextRun(xref.Label ?? xref.Target);

            case FootnoteInlineNode footnote:
                if (footnote.Text != null)
                    return new TextRun("[" + footnote.Text + "]");
                if (footnote.Id != null)
                    return new TextRun("[" + footnote.Id + "]");
                return null;

            case PassthroughInlineNode passthrough:
                return new TextRun(passthrough.Content);

            case SuperscriptInlineNode superscript:
                return new TextRun(superscript.Content);

            case SubscriptInlineNode subscript:
                return new TextRun(subscript.Content);

            case InlineAnchorNode:
                return null;

            case IndexTermNode indexTerm:
                return indexTerm.Terms.Count > 0 ? new TextRun(indexTerm.Terms[0]) : null;

            case IndexTermHiddenNode:
                return null;

            case InlineMacroNode macro:
                return new TextRun(macro.Content);

            case InlineImageNode image:
                return new TextRun("[image: " + image.Alt + "]");

            case InterDocumentXrefNode xdoc:
                return new TextRun(xdoc.Label ?? xdoc.Path);

            default:
                return null;
        }
    }
}
