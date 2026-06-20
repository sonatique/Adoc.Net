using System;
using System.Collections.Generic;
using AdocNet.Ast;

namespace AdocNet.Layout.Builders;

/// <summary>
/// Converts an AST <see cref="DocumentNode"/> into a <see cref="DocumentLayout"/> tree.
/// </summary>
/// <remarks>
/// A single <see cref="Build"/> call is self-contained: it carries transient
/// footnote-numbering state for the duration of that call only and clears it
/// before returning, so one builder instance can be reused across documents
/// (as the incremental renderer does). Output is a deterministic function of
/// the input document.
/// </remarks>
public class LayoutBuilder
{
    /// <summary>
    /// Footnotes collected during the current <see cref="Build"/> call. Non-null
    /// only while building; cleared before the footnotes area is rendered so a
    /// footnote nested inside a footnote body can't renumber the document.
    /// </summary>
    private FootnoteCollector? _footnotes;

    /// <summary>
    /// Builds a <see cref="DocumentLayout"/> from the given AST document.
    /// </summary>
    /// <param name="document">The parsed AST document.</param>
    /// <returns>A layout tree suitable for rendering.</returns>
    public DocumentLayout Build(DocumentNode document)
    {
        _footnotes = new FootnoteCollector();

        var blocks = new List<BlockLayout>();
        foreach (var child in document.Children)
        {
            BuildBlock(child, blocks);
        }

        // Footnote references become [n] markers in the body (see BuildInlineCore);
        // their bodies are collected here into a trailing footnotes area, matching
        // the HTML/PDF converters (issue #63).
        AppendFootnotesArea(blocks);

        _footnotes = null;
        return new DocumentLayout(document.Title, blocks);
    }

    private IReadOnlyList<BlockLayout> BuildBlocks(IReadOnlyList<AstNode> children)
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

    private void BuildBlock(AstNode node, List<BlockLayout> output)
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

    private void BuildSection(SectionNode section, List<BlockLayout> output)
    {
        var inlines = BuildInlines(section.TitleInlines, HeadingContentOrigin(section));
        output.Add(new HeadingLayout(section.Level, inlines) { Source = section.Source });

        foreach (var child in section.Children)
        {
            BuildBlock(child, output);
        }
    }

    private ParagraphLayout BuildParagraph(ParagraphNode paragraph)
    {
        // A paragraph's inline buffer begins exactly at its source start
        // (col 1, no marker), so the block start is the content origin.
        var inlines = BuildInlines(paragraph.Inlines, paragraph.Source.Start);
        return new ParagraphLayout(inlines) { Source = paragraph.Source };
    }

    private ListLayout BuildList(ListNode list)
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

    private ListItemLayout BuildListItem(ListItemNode item)
    {
        // The item's source range starts at the list marker; the inline text
        // begins after it. We don't have the marker width on the AST, so use
        // the item start as origin: line numbers are exact, and columns are
        // exact for items whose content begins at the marker column.
        var inlines = BuildInlines(item.Inlines, item.Source.Start);
        var blocks = BuildBlocks(item.Children);
        return new ListItemLayout(inlines, blocks);
    }

    private void BuildDelimitedBlock(DelimitedBlockNode delimited, List<BlockLayout> output)
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

    private AdmonitionLayout BuildAdmonition(AdmonitionNode admonition)
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

    private TableLayout BuildTable(TableNode table)
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

    private TableRowLayout BuildTableRow(TableRowNode rowNode, bool isHeaderRow)
    {
        var cells = new List<TableCellLayout>();
        foreach (var child in rowNode.Children)
        {
            if (child is TableCellNode cellNode)
            {
                // cellNode.Source is the cell's own content span (issue #45), so its
                // Start is the content origin: inline columns promote to absolute
                // document coordinates here, like every other inline since #38.
                var inlines = BuildInlines(cellNode.Inlines, cellNode.Source.Start);
                if (inlines.Count == 0 && !string.IsNullOrEmpty(cellNode.Text))
                {
                    // AsciiDoc (a|) cells expose no inline list (their content is a
                    // nested block tree). Surface the raw text as one run, tagged with
                    // the cell's absolute span so it has a source range rather than None.
                    inlines = new InlineLayout[] { new TextRun(cellNode.Text) { Source = cellNode.Source } };
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

    private DescriptionListLayout BuildDescriptionList(DescriptionListNode descList)
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

    /// <summary>
    /// Appends a trailing footnotes area (a thematic break followed by one
    /// paragraph per footnote, each prefixed with its <c>[n]</c> number) when the
    /// document defined any footnotes. Mirrors the HTML converter's
    /// <c>&lt;div id="footnotes"&gt;&lt;hr&gt;…</c> section so the live-preview
    /// path shows footnote bodies in a dedicated area rather than inlining them
    /// at the reference (issue #63).
    /// </summary>
    private void AppendFootnotesArea(List<BlockLayout> blocks)
    {
        var collector = _footnotes;
        if (collector is null || collector.Footnotes.Count == 0)
            return;

        // Clear the collector before rendering bodies so a footnote nested inside
        // a footnote body falls back to literal text instead of registering a new
        // (and renumbering an existing) entry.
        _footnotes = null;

        blocks.Add(new ThematicBreakLayout());
        foreach (var (number, _, node) in collector.Footnotes)
        {
            var inlines = new List<InlineLayout> { new TextRun("[" + number + "] ") };
            if (node.Inlines.Count > 0)
                inlines.AddRange(BuildInlines(node.Inlines, node.Source.Start));
            else if (node.Text is { Length: > 0 } bodyText)
                inlines.Add(new TextRun(bodyText));
            blocks.Add(new ParagraphLayout(inlines) { Source = node.Source });
        }
    }

    private IReadOnlyList<InlineLayout> BuildInlines(IReadOnlyList<InlineNode> nodes, SourcePosition contentOrigin)
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

    private InlineLayout? BuildInline(InlineNode node, SourcePosition contentOrigin)
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

    private InlineLayout? BuildInlineCore(InlineNode node, SourcePosition contentOrigin)
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
                return BuildFootnoteMarker(footnote);

            case PassthroughInlineNode passthrough:
                return new TextRun(passthrough.Content);

            case SuperscriptInlineNode superscript:
                return new SuperscriptRun(new InlineLayout[] { new TextRun(superscript.Content) });

            case SubscriptInlineNode subscript:
                return new SubscriptRun(new InlineLayout[] { new TextRun(subscript.Content) });

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

    /// <summary>
    /// Renders a footnote reference as a superscript <c>[n]</c> marker that links to
    /// its definition (matching the HTML/PDF converters: a raised, clickable marker)
    /// and registers its body for the trailing footnotes area. When the collector has
    /// been cleared — i.e. we are already rendering a footnote body — a nested footnote
    /// falls back to a plain superscript marker (no link/number) so its content is
    /// never silently dropped (issues #63, #71).
    /// </summary>
    private InlineLayout? BuildFootnoteMarker(FootnoteInlineNode footnote)
    {
        if (_footnotes is null)
        {
            if (footnote.Text is not null)
                return Superscript(new TextRun("[" + footnote.Text + "]"));
            if (footnote.Id is not null)
                return Superscript(new TextRun("[" + footnote.Id + "]"));
            return null;
        }

        int number = _footnotes.Register(footnote);
        // A superscript marker wrapping a link to the footnote definition, so the
        // Avalonia preview raises it and makes it navigable (LinkRun is clickable).
        return Superscript(new LinkRun(FootnoteDefHref(number),
            new InlineLayout[] { new TextRun("[" + number + "]") }));
    }

    private static SuperscriptRun Superscript(InlineLayout child) =>
        new(new[] { child });

    /// <summary>
    /// Anchor href of a footnote's definition in the trailing footnotes area,
    /// using the same <c>_footnotedef_N</c> convention as the HTML converter so a
    /// host can resolve the marker's link to the note.
    /// </summary>
    private static string FootnoteDefHref(int number) => "#_footnotedef_" + number;

    /// <summary>
    /// Assigns document-wide footnote numbers during a single build, mirroring
    /// the HTML converter's footnote state: anonymous footnotes get the next
    /// number, named footnotes (and their <c>footnote:id[]</c> back-references)
    /// reuse the first number seen for that id.
    /// </summary>
    private sealed class FootnoteCollector
    {
        public List<(int Number, string? Id, FootnoteInlineNode Node)> Footnotes { get; } = new();
        private int _next = 1;

        /// <summary>
        /// Registers a footnote and returns its display number. A named footnote
        /// or a back-reference whose id was already registered reuses the existing
        /// number (and does not add a second body entry).
        /// </summary>
        public int Register(FootnoteInlineNode node)
        {
            if (node.Id is not null)
            {
                foreach (var (num, id, _) in Footnotes)
                {
                    if (id == node.Id)
                        return num;
                }
            }

            int number = _next++;
            Footnotes.Add((number, node.Id, node));
            return number;
        }
    }
}
