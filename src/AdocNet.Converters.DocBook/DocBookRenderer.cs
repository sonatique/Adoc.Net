using System.Text;
using System.Xml;
using AdocNet.Ast;

namespace AdocNet.Converters.DocBook;

/// <summary>
/// Renders a <see cref="DocumentNode"/> AST to DocBook 5.0 XML.
/// Output is deterministic, using UTF-8 without BOM and consistent indentation.
/// </summary>
public sealed class DocBookRenderer : DocumentRendererBase
{
    private const string DocBookNs = "http://docbook.org/ns/docbook";
    private const string XLinkNs = "http://www.w3.org/1999/xlink";
    private const string XmlNs = "http://www.w3.org/XML/1998/namespace";

    /// <inheritdoc />
    public override string Format => "docbook";

    /// <inheritdoc />
    protected override void RenderDocument(RenderContext context, Stream output)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(false),
            OmitXmlDeclaration = false,
            NewLineChars = "\n",
        };

        using var writer = XmlWriter.Create(output, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("article", DocBookNs);
        writer.WriteAttributeString("version", "5.0");
        writer.WriteAttributeString("xmlns", "xlink", null, XLinkNs);

        if (context.Document.Title is not null)
        {
            writer.WriteElementString("title", DocBookNs, context.Document.Title);
        }

        foreach (var child in context.Document.Children)
        {
            if (child is BlockNode block)
                RenderBlock(writer, block, context);
        }

        writer.WriteEndElement(); // article
        writer.WriteEndDocument();
        writer.Flush();
    }

    // ── Block rendering ─────────────────────────────────────────────────

    private void RenderBlock(XmlWriter writer, BlockNode node, RenderContext context)
    {
        switch (node)
        {
            case SectionNode n: RenderSection(writer, n, context); break;
            case ParagraphNode n: RenderParagraph(writer, n, context); break;
            case ListNode n: RenderList(writer, n, context); break;
            case ListItemNode n: RenderListItem(writer, n, context); break;
            case TableNode n: RenderTable(writer, n, context); break;
            case DelimitedBlockNode n: RenderDelimitedBlock(writer, n, context); break;
            case BlockImageNode n: RenderBlockImage(writer, n, context); break;
            case AdmonitionNode n: RenderAdmonition(writer, n, context); break;
            case DescriptionListNode n: RenderDescriptionList(writer, n, context); break;
            case DescriptionItemNode n: RenderDescriptionItem(writer, n, context); break;
            case ThematicBreakNode: break; // No direct DocBook equivalent — skip
            case PageBreakNode: writer.WriteProcessingInstruction("hard-pagebreak", ""); break;
            case TocNode: break; // DocBook processors generate TOC automatically
            case VideoNode n: RenderVideo(writer, n, context); break;
            case AudioNode n: RenderAudio(writer, n, context); break;
            case IndexNode: writer.WriteStartElement("index", DocBookNs); writer.WriteEndElement(); break;
            case BibliographyEntryNode n: RenderBibliographyEntry(writer, n, context); break;
            default: break;
        }
    }

    private void RenderSection(XmlWriter writer, SectionNode node, RenderContext context)
    {
        writer.WriteStartElement("section", DocBookNs);

        if (node.Id is not null)
            writer.WriteAttributeString("xml", "id", XmlNs, node.Id);

        writer.WriteStartElement("title", DocBookNs);
        if (node.TitleInlines.Count > 0)
        {
            RenderInlines(writer, node.TitleInlines, context);
        }
        else
        {
            writer.WriteString(node.Title);
        }
        writer.WriteEndElement(); // title

        foreach (var child in node.Children)
        {
            if (child is BlockNode block)
                RenderBlock(writer, block, context);
        }

        writer.WriteEndElement(); // section
    }

    private void RenderParagraph(XmlWriter writer, ParagraphNode node, RenderContext context)
    {
        writer.WriteStartElement("para", DocBookNs);

        if (node.Inlines.Count > 0)
        {
            RenderInlines(writer, node.Inlines, context);
        }
        else
        {
            writer.WriteString(node.Text);
        }

        writer.WriteEndElement(); // para
    }

    private void RenderList(XmlWriter writer, ListNode node, RenderContext context)
    {
        var elementName = node.ListKind == ListKind.Ordered ? "orderedlist" : "itemizedlist";
        writer.WriteStartElement(elementName, DocBookNs);

        foreach (var child in node.Children)
        {
            if (child is ListItemNode item)
                RenderListItem(writer, item, context);
        }

        writer.WriteEndElement();
    }

    private void RenderListItem(XmlWriter writer, ListItemNode node, RenderContext context)
    {
        writer.WriteStartElement("listitem", DocBookNs);

        // If the list item has nested block children (e.g. nested lists), render them.
        // Otherwise wrap inline content in a <para>.
        var hasBlockChildren = node.Children.Any(c => c is BlockNode);

        if (hasBlockChildren)
        {
            // Wrap inline content in a para first
            if (node.Inlines.Count > 0 || node.Text.Length > 0)
            {
                writer.WriteStartElement("para", DocBookNs);
                if (node.Inlines.Count > 0)
                    RenderInlines(writer, node.Inlines, context);
                else
                    writer.WriteString(node.Text);
                writer.WriteEndElement(); // para
            }

            foreach (var child in node.Children)
            {
                if (child is BlockNode block)
                    RenderBlock(writer, block, context);
            }
        }
        else
        {
            writer.WriteStartElement("para", DocBookNs);
            if (node.Inlines.Count > 0)
                RenderInlines(writer, node.Inlines, context);
            else
                writer.WriteString(node.Text);
            writer.WriteEndElement(); // para
        }

        writer.WriteEndElement(); // listitem
    }

    private void RenderTable(XmlWriter writer, TableNode node, RenderContext context)
    {
        var hasTitle = node.Title is not null;
        writer.WriteStartElement(hasTitle ? "table" : "informaltable", DocBookNs);

        if (hasTitle)
            writer.WriteElementString("title", DocBookNs, node.Title);

        // Determine column count from first row
        var rows = node.Children.OfType<TableRowNode>().ToList();
        var colCount = rows.Count > 0
            ? rows[0].Children.OfType<TableCellNode>().Count()
            : 0;

        writer.WriteStartElement("tgroup", DocBookNs);
        writer.WriteAttributeString("cols", colCount.ToString());

        // Write colspec elements
        if (node.Columns is not null)
        {
            foreach (var col in node.Columns)
            {
                writer.WriteStartElement("colspec", DocBookNs);
                writer.WriteAttributeString("colwidth", $"{col.Width}*");
                writer.WriteEndElement(); // colspec
            }
        }
        else
        {
            for (var i = 0; i < colCount; i++)
            {
                writer.WriteStartElement("colspec", DocBookNs);
                writer.WriteAttributeString("colwidth", "1*");
                writer.WriteEndElement(); // colspec
            }
        }

        // Header row
        if (node.HasHeader && rows.Count > 0)
        {
            writer.WriteStartElement("thead", DocBookNs);
            RenderTableRow(writer, rows[0], context);
            writer.WriteEndElement(); // thead
            rows = rows.Skip(1).ToList();
        }

        // Footer row
        TableRowNode? footerRow = null;
        if (node.HasFooter && rows.Count > 0)
        {
            footerRow = rows[^1];
            rows = rows.Take(rows.Count - 1).ToList();
        }

        // Body rows
        writer.WriteStartElement("tbody", DocBookNs);
        foreach (var row in rows)
        {
            RenderTableRow(writer, row, context);
        }
        writer.WriteEndElement(); // tbody

        // Footer
        if (footerRow is not null)
        {
            writer.WriteStartElement("tfoot", DocBookNs);
            RenderTableRow(writer, footerRow, context);
            writer.WriteEndElement(); // tfoot
        }

        writer.WriteEndElement(); // tgroup
        writer.WriteEndElement(); // table or informaltable
    }

    private void RenderTableRow(XmlWriter writer, TableRowNode row, RenderContext context)
    {
        writer.WriteStartElement("row", DocBookNs);
        foreach (var child in row.Children)
        {
            if (child is TableCellNode cell)
            {
                writer.WriteStartElement("entry", DocBookNs);

                if (cell.ColSpan > 1)
                    writer.WriteAttributeString("namest", $"col{1}");

                if (cell.Inlines.Count > 0)
                    RenderInlines(writer, cell.Inlines, context);
                else
                    writer.WriteString(cell.Text);

                writer.WriteEndElement(); // entry
            }
        }
        writer.WriteEndElement(); // row
    }

    private void RenderDelimitedBlock(XmlWriter writer, DelimitedBlockNode node, RenderContext context)
    {
        switch (node.BlockKind)
        {
            case DelimitedBlockKind.Source:
            case DelimitedBlockKind.Listing:
                writer.WriteStartElement("programlisting", DocBookNs);
                if (node.Language is not null)
                    writer.WriteAttributeString("language", node.Language);
                writer.WriteString(node.Content ?? "");
                writer.WriteEndElement();
                break;

            case DelimitedBlockKind.Literal:
                writer.WriteStartElement("literallayout", DocBookNs);
                writer.WriteString(node.Content ?? "");
                writer.WriteEndElement();
                break;

            case DelimitedBlockKind.Example:
                writer.WriteStartElement("example", DocBookNs);
                if (node.Title is not null)
                    writer.WriteElementString("title", DocBookNs, node.Title);
                foreach (var child in node.Children)
                {
                    if (child is BlockNode block)
                        RenderBlock(writer, block, context);
                }
                writer.WriteEndElement();
                break;

            case DelimitedBlockKind.Quote:
                writer.WriteStartElement("blockquote", DocBookNs);
                if (node.Attribution is not null)
                {
                    writer.WriteStartElement("attribution", DocBookNs);
                    writer.WriteString(node.Attribution);
                    writer.WriteEndElement();
                }
                foreach (var child in node.Children)
                {
                    if (child is BlockNode block)
                        RenderBlock(writer, block, context);
                }
                writer.WriteEndElement();
                break;

            case DelimitedBlockKind.Sidebar:
                writer.WriteStartElement("sidebar", DocBookNs);
                if (node.Title is not null)
                    writer.WriteElementString("title", DocBookNs, node.Title);
                foreach (var child in node.Children)
                {
                    if (child is BlockNode block)
                        RenderBlock(writer, block, context);
                }
                writer.WriteEndElement();
                break;

            case DelimitedBlockKind.Verse:
                writer.WriteStartElement("literallayout", DocBookNs);
                writer.WriteString(node.Content ?? "");
                writer.WriteEndElement();
                break;

            case DelimitedBlockKind.Open:
                // Open blocks just pass through their children
                foreach (var child in node.Children)
                {
                    if (child is BlockNode block)
                        RenderBlock(writer, block, context);
                }
                break;

            case DelimitedBlockKind.Passthrough:
                // Passthrough content is written as raw XML
                if (node.Content is not null)
                    writer.WriteRaw(node.Content);
                break;
        }
    }

    private void RenderBlockImage(XmlWriter writer, BlockImageNode node, RenderContext context)
    {
        writer.WriteStartElement("figure", DocBookNs);

        if (node.Id is not null)
            writer.WriteAttributeString("xml", "id", XmlNs, node.Id);

        if (node.Title is not null)
            writer.WriteElementString("title", DocBookNs, node.Title);

        writer.WriteStartElement("mediaobject", DocBookNs);
        writer.WriteStartElement("imageobject", DocBookNs);
        writer.WriteStartElement("imagedata", DocBookNs);
        writer.WriteAttributeString("fileref", node.Target);
        writer.WriteEndElement(); // imagedata
        writer.WriteEndElement(); // imageobject

        if (!string.IsNullOrEmpty(node.Alt))
        {
            writer.WriteStartElement("textobject", DocBookNs);
            writer.WriteElementString("phrase", DocBookNs, node.Alt);
            writer.WriteEndElement(); // textobject
        }

        writer.WriteEndElement(); // mediaobject
        writer.WriteEndElement(); // figure
    }

    private void RenderAdmonition(XmlWriter writer, AdmonitionNode node, RenderContext context)
    {
        var elementName = node.AdmonitionType.ToLowerInvariant() switch
        {
            "note" => "note",
            "tip" => "tip",
            "warning" => "warning",
            "caution" => "caution",
            "important" => "important",
            _ => "note",
        };

        writer.WriteStartElement(elementName, DocBookNs);

        if (node.Inlines.Count > 0)
        {
            writer.WriteStartElement("para", DocBookNs);
            RenderInlines(writer, node.Inlines, context);
            writer.WriteEndElement(); // para
        }
        else if (node.Text is not null)
        {
            writer.WriteStartElement("para", DocBookNs);
            writer.WriteString(node.Text);
            writer.WriteEndElement(); // para
        }

        // Block admonitions have children
        foreach (var child in node.Children)
        {
            if (child is BlockNode block)
                RenderBlock(writer, block, context);
        }

        writer.WriteEndElement();
    }

    private void RenderDescriptionList(XmlWriter writer, DescriptionListNode node, RenderContext context)
    {
        writer.WriteStartElement("variablelist", DocBookNs);

        foreach (var child in node.Children)
        {
            if (child is DescriptionItemNode item)
                RenderDescriptionItem(writer, item, context);
        }

        writer.WriteEndElement(); // variablelist
    }

    private void RenderDescriptionItem(XmlWriter writer, DescriptionItemNode node, RenderContext context)
    {
        writer.WriteStartElement("varlistentry", DocBookNs);

        writer.WriteStartElement("term", DocBookNs);
        if (node.TermInlines.Count > 0)
            RenderInlines(writer, node.TermInlines, context);
        else
            writer.WriteString(node.Terms[0]);
        writer.WriteEndElement(); // term

        writer.WriteStartElement("listitem", DocBookNs);
        writer.WriteStartElement("para", DocBookNs);
        if (node.DescriptionInlines.Count > 0)
            RenderInlines(writer, node.DescriptionInlines, context);
        else
            writer.WriteString(node.Description);
        writer.WriteEndElement(); // para

        // Render any block children
        foreach (var child in node.Children)
        {
            if (child is BlockNode block)
                RenderBlock(writer, block, context);
        }

        writer.WriteEndElement(); // listitem
        writer.WriteEndElement(); // varlistentry
    }

    private void RenderVideo(XmlWriter writer, VideoNode node, RenderContext context)
    {
        writer.WriteStartElement("mediaobject", DocBookNs);
        writer.WriteStartElement("videoobject", DocBookNs);
        writer.WriteStartElement("videodata", DocBookNs);
        writer.WriteAttributeString("fileref", node.Target);
        if (node.Width is not null)
            writer.WriteAttributeString("width", node.Width);
        if (node.Height is not null)
            writer.WriteAttributeString("depth", node.Height);
        writer.WriteEndElement(); // videodata
        writer.WriteEndElement(); // videoobject
        writer.WriteEndElement(); // mediaobject
    }

    private void RenderAudio(XmlWriter writer, AudioNode node, RenderContext context)
    {
        writer.WriteStartElement("mediaobject", DocBookNs);
        writer.WriteStartElement("audioobject", DocBookNs);
        writer.WriteStartElement("audiodata", DocBookNs);
        writer.WriteAttributeString("fileref", node.Target);
        writer.WriteEndElement(); // audiodata
        writer.WriteEndElement(); // audioobject
        writer.WriteEndElement(); // mediaobject
    }

    private void RenderBibliographyEntry(XmlWriter writer, BibliographyEntryNode node, RenderContext context)
    {
        writer.WriteStartElement("bibliomixed", DocBookNs);
        writer.WriteAttributeString("xml", "id", XmlNs, node.RefId);

        writer.WriteStartElement("bibliomisc", DocBookNs);
        if (node.Inlines.Count > 0)
            RenderInlines(writer, node.Inlines, context);
        else
            writer.WriteString(node.Text);
        writer.WriteEndElement(); // bibliomisc

        writer.WriteEndElement(); // bibliomixed
    }

    // ── Inline rendering ────────────────────────────────────────────────

    private void RenderInlines(XmlWriter writer, IEnumerable<InlineNode> nodes, RenderContext context)
    {
        foreach (var node in nodes)
            RenderInline(writer, node, context);
    }

    private void RenderInline(XmlWriter writer, InlineNode node, RenderContext context)
    {
        switch (node)
        {
            case TextInlineNode n:
                writer.WriteString(n.Value);
                break;

            case StrongInlineNode n:
                writer.WriteStartElement("emphasis", DocBookNs);
                writer.WriteAttributeString("role", "strong");
                RenderInlines(writer, n.Children, context);
                writer.WriteEndElement();
                break;

            case EmphasisInlineNode n:
                writer.WriteStartElement("emphasis", DocBookNs);
                RenderInlines(writer, n.Children, context);
                writer.WriteEndElement();
                break;

            case MonospaceInlineNode n:
                writer.WriteStartElement("literal", DocBookNs);
                RenderInlines(writer, n.Children, context);
                writer.WriteEndElement();
                break;

            case LinkInlineNode n:
                writer.WriteStartElement("link", DocBookNs);
                writer.WriteAttributeString("xlink", "href", XLinkNs, n.Url);
                writer.WriteString(n.Url);
                writer.WriteEndElement();
                break;

            case InlineLinkMacroNode n:
                writer.WriteStartElement("link", DocBookNs);
                writer.WriteAttributeString("xlink", "href", XLinkNs, n.Url);
                writer.WriteString(n.Label.Length > 0 ? n.Label : n.Url);
                writer.WriteEndElement();
                break;

            case InlineImageNode n:
                writer.WriteStartElement("inlinemediaobject", DocBookNs);
                writer.WriteStartElement("imageobject", DocBookNs);
                writer.WriteStartElement("imagedata", DocBookNs);
                writer.WriteAttributeString("fileref", n.Target);
                writer.WriteEndElement(); // imagedata
                writer.WriteEndElement(); // imageobject
                if (!string.IsNullOrEmpty(n.Alt))
                {
                    writer.WriteStartElement("textobject", DocBookNs);
                    writer.WriteElementString("phrase", DocBookNs, n.Alt);
                    writer.WriteEndElement(); // textobject
                }
                writer.WriteEndElement(); // inlinemediaobject
                break;

            case FootnoteInlineNode n:
                writer.WriteStartElement("footnote", DocBookNs);
                writer.WriteStartElement("para", DocBookNs);
                if (n.Inlines.Count > 0)
                    RenderInlines(writer, n.Inlines, context);
                else if (n.Text is not null)
                    writer.WriteString(n.Text);
                writer.WriteEndElement(); // para
                writer.WriteEndElement(); // footnote
                break;

            case CrossReferenceInlineNode n:
                if (n.Label is not null)
                {
                    writer.WriteStartElement("link", DocBookNs);
                    writer.WriteAttributeString("linkend", n.Target);
                    writer.WriteString(n.Label);
                    writer.WriteEndElement();
                }
                else
                {
                    writer.WriteStartElement("xref", DocBookNs);
                    writer.WriteAttributeString("linkend", n.Target);
                    writer.WriteEndElement();
                }
                break;

            case InterDocumentXrefNode n:
                writer.WriteStartElement("olink", DocBookNs);
                writer.WriteAttributeString("targetdoc", n.Path);
                if (n.Id is not null)
                    writer.WriteAttributeString("targetptr", n.Id);
                if (n.Label is not null)
                    writer.WriteString(n.Label);
                writer.WriteEndElement();
                break;

            case SuperscriptInlineNode n:
                writer.WriteStartElement("superscript", DocBookNs);
                writer.WriteString(n.Content);
                writer.WriteEndElement();
                break;

            case SubscriptInlineNode n:
                writer.WriteStartElement("subscript", DocBookNs);
                writer.WriteString(n.Content);
                writer.WriteEndElement();
                break;

            case PassthroughInlineNode n:
                writer.WriteRaw(n.Content);
                break;

            case HighlightInlineNode n:
                writer.WriteStartElement("emphasis", DocBookNs);
                writer.WriteAttributeString("role", "marked");
                RenderInlines(writer, n.Children, context);
                writer.WriteEndElement();
                break;

            case IndexTermNode n:
                RenderIndexTermInlines(writer, n.Terms);
                // Visible index term: also write the primary term text
                if (n.Terms.Count > 0)
                    writer.WriteString(n.Terms[0]);
                break;

            case IndexTermHiddenNode n:
                RenderIndexTermInlines(writer, n.Terms);
                break;

            case InlineAnchorNode n:
                writer.WriteStartElement("anchor", DocBookNs);
                writer.WriteAttributeString("xml", "id", XmlNs, n.Id);
                writer.WriteEndElement();
                break;

            case InlineMacroNode n:
                RenderInlineMacro(writer, n, context);
                break;

            default:
                break;
        }
    }

    private void RenderIndexTermInlines(XmlWriter writer, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
            return;

        writer.WriteStartElement("indexterm", DocBookNs);
        writer.WriteElementString("primary", DocBookNs, terms[0]);
        if (terms.Count > 1)
            writer.WriteElementString("secondary", DocBookNs, terms[1]);
        if (terms.Count > 2)
            writer.WriteElementString("tertiary", DocBookNs, terms[2]);
        writer.WriteEndElement(); // indexterm
    }

    private void RenderInlineMacro(XmlWriter writer, InlineMacroNode node, RenderContext context)
    {
        switch (node.Name)
        {
            case "kbd":
                writer.WriteStartElement("keycap", DocBookNs);
                writer.WriteString(node.Content);
                writer.WriteEndElement();
                break;

            case "menu":
                writer.WriteStartElement("menuchoice", DocBookNs);
                writer.WriteElementString("guimenu", DocBookNs, node.Target);
                writer.WriteElementString("guimenuitem", DocBookNs, node.Content);
                writer.WriteEndElement();
                break;

            case "btn":
                writer.WriteStartElement("guibutton", DocBookNs);
                writer.WriteString(node.Content);
                writer.WriteEndElement();
                break;

            default:
                // Generic fallback: just write content
                writer.WriteString(node.Content);
                break;
        }
    }
}
