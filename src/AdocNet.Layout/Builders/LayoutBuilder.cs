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
        var inlines = BuildInlines(section.TitleInlines, HeadingContentOrigin(section));
        output.Add(new HeadingLayout(section.Level, inlines) { Source = section.Source });

        foreach (var child in section.Children)
        {
            BuildBlock(child, output);
        }
    }

    private static ParagraphLayout BuildParagraph(ParagraphNode paragraph)
    {
        // A paragraph's inline buffer begins exactly at its source start
        // (col 1, no marker), so the block start is the content origin.
        var inlines = BuildInlines(paragraph.Inlines, paragraph.Source.Start);
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
        // The item's source range starts at the list marker; the inline text
        // begins after it. We don't have the marker width on the AST, so use
        // the item start as origin: line numbers are exact, and columns are
        // exact for items whose content begins at the marker column.
        var inlines = BuildInlines(item.Inlines, item.Source.Start);
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
            var inlines = BuildInlines(admonition.Inlines, admonition.Source.Start);
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
                var inlines = BuildInlines(cellNode.Inlines, cellNode.Source.Start);
                if (inlines.Count == 0 && !string.IsNullOrEmpty(cellNode.Text))
                {
                    inlines = new InlineLayout[] { new TextRun(cellNode.Text) };
                }
                bool isHeader = isHeaderRow || cellNode.ContentStyle == TableCellStyle.Header;
                cells.Add(new TableCellLayout(inlines, cellNode.ColSpan, cellNode.RowSpan, isHeader)
                {
                    Source = cellNode.Source,
                });
            }
        }
        return new TableRowLayout(cells) { Source = rowNode.Source };
    }

    private static DescriptionListLayout BuildDescriptionList(DescriptionListNode descList)
    {
        var items = new List<DescriptionItemLayout>();
        foreach (var child in descList.Children)
        {
            if (child is DescriptionItemNode item)
            {
                // The term begins at the item's source start; the description
                // follows the `::` delimiter. We only have the item start, so
                // both use it as origin — exact lines, exact term column, and
                // best-effort description column.
                var origin = item.Source.Start;
                var term = BuildInlines(item.TermInlines, origin);
                if (term.Count == 0 && item.Terms.Count > 0 && !string.IsNullOrEmpty(item.Terms[0]))
                    term = new InlineLayout[] { new TextRun(item.Terms[0]) };

                var desc = BuildInlines(item.DescriptionInlines, origin);
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

    private static IReadOnlyList<InlineLayout> BuildInlines(IReadOnlyList<InlineNode> nodes, SourcePosition contentOrigin)
    {
        if (nodes.Count == 0)
            return Array.Empty<InlineLayout>();

        var result = new List<InlineLayout>();
        foreach (var node in nodes)
        {
            var inline = BuildInline(node, contentOrigin);
            if (inline != null)
            {
                result.Add(inline);
            }
        }
        return result;
    }

    private static InlineLayout? BuildInline(InlineNode node, SourcePosition contentOrigin)
    {
        // Stamp the source range once here so every inline layout node carries
        // it (for editor caret/selection ↔ source mapping) without repeating the
        // assignment in each switch arm below.
        //
        // InlineParser produces ranges that are RELATIVE to the block's inline
        // text buffer (line 1, col 1 at the start of that buffer). Promote them
        // to absolute document coordinates here using the owning block's
        // content origin — otherwise every inline would report line 1 and an
        // editor doing click-to-source would resolve every click to the first
        // line of the document (issue #38).
        var layout = BuildInlineCore(node, contentOrigin);
        if (layout is not null)
            layout.Source = ToAbsolute(node.Source, contentOrigin);
        return layout;
    }

    /// <summary>
    /// Promotes a block-relative inline <see cref="SourceRange"/> to absolute
    /// document coordinates given the absolute position of relative
    /// <c>(line 1, col 1)</c> of the owning block's inline text buffer.
    /// </summary>
    /// <remarks>
    /// The line component composes for every line of the buffer. The column
    /// composes with the origin only on the buffer's first line; on a
    /// continuation line the relative column already equals the source column,
    /// because the buffer preserves the source's own line breaks. If either the
    /// range or the origin is <see cref="SourceRange.None"/> / unknown, the
    /// range is returned unchanged so callers without a reliable origin keep
    /// the previous (relative) value rather than a corrupted one.
    /// </remarks>
    internal static SourceRange ToAbsolute(SourceRange relative, SourcePosition contentOrigin)
    {
        if (relative.IsNone || contentOrigin.IsNone)
            return relative;
        return new SourceRange(
            OffsetPosition(relative.Start, contentOrigin),
            OffsetPosition(relative.End, contentOrigin));
    }

    private static SourcePosition OffsetPosition(SourcePosition p, SourcePosition origin)
    {
        if (p.IsNone)
            return p;
        int line = origin.Line + (p.Line - 1);
        int column = p.Line == 1 ? origin.Column + (p.Column - 1) : p.Column;
        return new SourcePosition(line, column);
    }

    /// <summary>
    /// Absolute source position of the first character of a section heading's
    /// title text — i.e. just past the <c>== </c> marker. The ATX marker is
    /// <c>(Level + 1)</c> equals signs followed by one space.
    /// </summary>
    private static SourcePosition HeadingContentOrigin(SectionNode section)
    {
        var start = section.Source.Start;
        if (start.IsNone)
            return SourcePosition.None;
        int markerWidth = section.Level + 2; // (Level + 1) '=' + 1 space
        return new SourcePosition(start.Line, start.Column + markerWidth);
    }

    private static InlineLayout? BuildInlineCore(InlineNode node, SourcePosition contentOrigin)
    {
        switch (node)
        {
            case TextInlineNode text:
                return new TextRun(text.Value);

            case StrongInlineNode strong:
                return new BoldRun(BuildInlines(strong.Children, contentOrigin));

            case EmphasisInlineNode emphasis:
                return new ItalicRun(BuildInlines(emphasis.Children, contentOrigin));

            case MonospaceInlineNode mono:
                return new MonoRun(BuildInlines(mono.Children, contentOrigin));

            case HighlightInlineNode highlight:
                return new BoldRun(BuildInlines(highlight.Children, contentOrigin));

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
