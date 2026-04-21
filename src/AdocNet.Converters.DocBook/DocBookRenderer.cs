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

    private int _calloutGroupCounter;

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

        _calloutGroupCounter = 0;

        using var writer = XmlWriter.Create(output, settings);

        writer.WriteStartDocument();
        writer.WriteStartElement("article", DocBookNs);
        writer.WriteAttributeString("version", "5.0");
        writer.WriteAttributeString("xmlns", "xlink", null, XLinkNs);
        // Asciidoctor adds xml:lang on the root, defaulting to "en". Honour the document's
        // :lang: attribute when set.
        var lang = context.Document.Attributes.TryGetValue("lang", out var l) && !string.IsNullOrWhiteSpace(l)
            ? l : "en";
        writer.WriteAttributeString("xml", "lang", XmlNs, lang);

        // Document metadata: <info><title/><date/></info> wrapper, matching Asciidoctor.
        // Bare <title> without <info> is also valid DocBook but Asciidoctor always wraps.
        // Date precedence (Asciidoctor parity): :revdate: → :docdate: → omit.
        // :reproducible: opt-out: when set, the date is suppressed entirely.
        if (context.Document.Title is not null)
        {
            writer.WriteStartElement("info", DocBookNs);
            writer.WriteElementString("title", DocBookNs, context.Document.Title);

            var attrs = context.Document.Attributes;
            var reproducible = attrs.ContainsKey("reproducible");
            if (!reproducible)
            {
                string? date = null;
                if (attrs.TryGetValue("revdate", out var rd) && !string.IsNullOrWhiteSpace(rd))
                    date = rd;
                else if (attrs.TryGetValue("docdate", out var dd) && !string.IsNullOrWhiteSpace(dd))
                    date = dd;
                if (date is not null)
                    writer.WriteElementString("date", DocBookNs, date);
            }

            writer.WriteEndElement(); // info
        }

        // Render children with section nesting:
        // DocBook requires sections to be nested (level-2 inside level-1, etc.)
        // while the AST stores them as flat siblings. Build the nesting here.
        RenderChildrenWithSectionNesting(writer, context.Document.Children, context);

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

    /// <summary>
    /// Renders a flat list of AST nodes with proper DocBook section nesting.
    /// Subsections (higher level number) are nested inside their parent sections.
    /// </summary>
    private void RenderChildrenWithSectionNesting(
        XmlWriter writer, IReadOnlyList<AstNode> children, RenderContext context)
    {
        int i = 0;
        while (i < children.Count)
        {
            if (children[i] is SectionNode section)
            {
                RenderSectionWithNesting(writer, section, children, ref i, context);
            }
            else if (children[i] is BlockNode block)
            {
                RenderBlock(writer, block, context);
                i++;
            }
            else
            {
                i++;
            }
        }
    }

    private void RenderSectionWithNesting(
        XmlWriter writer, SectionNode node, IReadOnlyList<AstNode> siblings,
        ref int index, RenderContext context)
    {
        // Detect bibliography sections (sections containing BibliographyEntryNode children)
        bool isBibliography = node.Children.Any(c => c is BibliographyEntryNode);

        writer.WriteStartElement(isBibliography ? "bibliography" : "section", DocBookNs);

        if (node.Id is not null)
            writer.WriteAttributeString("xml", "id", XmlNs, node.Id);

        WriteRoles(writer, node);

        writer.WriteStartElement("title", DocBookNs);
        if (node.TitleInlines.Count > 0)
            RenderInlines(writer, node.TitleInlines, context);
        else
            writer.WriteString(node.Title);
        writer.WriteEndElement(); // title

        if (isBibliography)
        {
            // Wrap bibliography entries in <bibliodiv>
            writer.WriteStartElement("bibliodiv", DocBookNs);
            foreach (var child in node.Children)
            {
                if (child is BibliographyEntryNode entry)
                    RenderBibliographyEntry(writer, entry, context);
                else if (child is BlockNode block)
                    RenderBlock(writer, block, context);
            }
            writer.WriteEndElement(); // bibliodiv
        }
        else
        {
            foreach (var child in node.Children)
            {
                if (child is BlockNode block)
                    RenderBlock(writer, block, context);
            }
        }

        // Consume subsequent sibling sections that are subsections (deeper level)
        int currentLevel = node.Level;
        index++;

        while (index < siblings.Count)
        {
            if (siblings[index] is SectionNode nextSection && nextSection.Level > currentLevel)
            {
                RenderSectionWithNesting(writer, nextSection, siblings, ref index, context);
            }
            else if (siblings[index] is SectionNode)
            {
                break;
            }
            else if (siblings[index] is BlockNode block)
            {
                RenderBlock(writer, block, context);
                index++;
            }
            else
            {
                index++;
            }
        }

        writer.WriteEndElement(); // section or bibliography
    }

    private void RenderSection(XmlWriter writer, SectionNode node, RenderContext context)
    {
        // Fallback for sections rendered outside the nesting context (e.g., inside blocks)
        writer.WriteStartElement("section", DocBookNs);

        if (node.Id is not null)
            writer.WriteAttributeString("xml", "id", XmlNs, node.Id);

        WriteRoles(writer, node);

        writer.WriteStartElement("title", DocBookNs);
        if (node.TitleInlines.Count > 0)
            RenderInlines(writer, node.TitleInlines, context);
        else
            writer.WriteString(node.Title);
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
        // DocBook5 distinguishes <simpara> (inline-only paragraphs) from <para> (can contain
        // nested blocks). Asciidoctor emits <simpara> for body paragraphs since they always
        // hold inline-only content. Match that to keep DocBook output portable to the same
        // downstream toolchains.
        writer.WriteStartElement("simpara", DocBookNs);

        if (node.Id is not null)
            writer.WriteAttributeString("xml", "id", XmlNs, node.Id);

        WriteRoles(writer, node);

        if (node.Inlines.Count > 0)
        {
            RenderInlines(writer, node.Inlines, context);
        }
        else
        {
            writer.WriteString(node.Text);
        }

        writer.WriteEndElement(); // simpara
    }

    private void RenderList(XmlWriter writer, ListNode node, RenderContext context, int orderedDepth = 0)
    {
        var elementName = node.ListKind == ListKind.Ordered ? "orderedlist" : "itemizedlist";
        writer.WriteStartElement(elementName, DocBookNs);

        if (node.Id is not null)
            writer.WriteAttributeString("xml", "id", XmlNs, node.Id);

        if (node.ListKind == ListKind.Ordered)
        {
            // Cycle numeration: arabic → loweralpha → lowerroman → upperalpha → upperroman
            // Only counts ordered list ancestors, not unordered ones
            var numeration = (orderedDepth % 5) switch
            {
                0 => "arabic",
                1 => "loweralpha",
                2 => "lowerroman",
                3 => "upperalpha",
                _ => "upperroman",
            };
            writer.WriteAttributeString("numeration", numeration);
        }

        // Pass the next ordered depth for children: increment only if THIS list is ordered
        var childOrderedDepth = node.ListKind == ListKind.Ordered ? orderedDepth + 1 : orderedDepth;

        foreach (var child in node.Children)
        {
            if (child is ListItemNode item)
                RenderListItem(writer, item, context, childOrderedDepth);
        }

        writer.WriteEndElement();
    }

    private void RenderListItem(XmlWriter writer, ListItemNode node, RenderContext context, int orderedDepth = 0)
    {
        writer.WriteStartElement("listitem", DocBookNs);

        // Asciidoctor always emits <simpara> for the item's inline text, even when
        // continuation blocks (e.g. listings, nested lists) follow as siblings inside
        // the <listitem>. <para> is reserved for paragraphs that themselves contain
        // nested block content — which a list item's inline text never does.
        if (node.Inlines.Count > 0 || node.Text.Length > 0)
        {
            writer.WriteStartElement("simpara", DocBookNs);
            if (node.Inlines.Count > 0)
                RenderInlines(writer, node.Inlines, context);
            else
                writer.WriteString(node.Text);
            writer.WriteEndElement(); // simpara
        }

        foreach (var child in node.Children)
        {
            if (child is ListNode nestedList)
                RenderList(writer, nestedList, context, orderedDepth);
            else if (child is BlockNode block)
                RenderBlock(writer, block, context);
        }

        writer.WriteEndElement(); // listitem
    }

    private void RenderTable(XmlWriter writer, TableNode node, RenderContext context)
    {
        var hasTitle = node.Title is not null;
        writer.WriteStartElement(hasTitle ? "table" : "informaltable", DocBookNs);

        if (node.Id is not null)
            writer.WriteAttributeString("xml", "id", XmlNs, node.Id);

        if (hasTitle)
            writer.WriteElementString("title", DocBookNs, node.Title);

        // Determine actual column count from column specs or first row
        var rows = node.Children.OfType<TableRowNode>().ToList();
        int colCount;
        if (node.Columns is not null)
        {
            colCount = node.Columns.Count;
        }
        else
        {
            // Count actual columns considering colspan
            colCount = rows.Count > 0
                ? rows[0].Children.OfType<TableCellNode>().Sum(c => c.ColSpan)
                : 0;
        }

        writer.WriteStartElement("tgroup", DocBookNs);
        writer.WriteAttributeString("cols", colCount.ToString());

        // Write colspec elements with proportional widths
        if (node.Columns is not null)
        {
            for (int i = 0; i < node.Columns.Count; i++)
            {
                writer.WriteStartElement("colspec", DocBookNs);
                writer.WriteAttributeString("colname", $"col_{i + 1}");
                writer.WriteAttributeString("colwidth", $"{node.Columns[i].Width}*");
                writer.WriteEndElement(); // colspec
            }
        }
        else
        {
            var defaultWidth = colCount > 0 ? 100 / colCount : 1;
            for (var i = 0; i < colCount; i++)
            {
                writer.WriteStartElement("colspec", DocBookNs);
                writer.WriteAttributeString("colname", $"col_{i + 1}");
                writer.WriteAttributeString("colwidth", $"{defaultWidth}*");
                writer.WriteEndElement(); // colspec
            }
        }

        // Header row
        if (node.HasHeader && rows.Count > 0)
        {
            writer.WriteStartElement("thead", DocBookNs);
            RenderTableRow(writer, rows[0], node, context, isHeader: true);
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
            RenderTableRow(writer, row, node, context);
        }
        writer.WriteEndElement(); // tbody

        // Footer
        if (footerRow is not null)
        {
            writer.WriteStartElement("tfoot", DocBookNs);
            RenderTableRow(writer, footerRow, node, context);
            writer.WriteEndElement(); // tfoot
        }

        writer.WriteEndElement(); // tgroup
        writer.WriteEndElement(); // table or informaltable
    }

    private void RenderTableRow(XmlWriter writer, TableRowNode row, TableNode table, RenderContext context, bool isHeader = false)
    {
        writer.WriteStartElement("row", DocBookNs);

        int colIndex = 0;
        foreach (var child in row.Children)
        {
            if (child is TableCellNode cell)
            {
                writer.WriteStartElement("entry", DocBookNs);

                // Resolve alignment from cell or column spec
                var hAlign = cell.Alignment;
                var vAlign = cell.VerticalAlignment;
                if (hAlign is null && table.Columns is not null && colIndex < table.Columns.Count)
                    hAlign = table.Columns[colIndex].Alignment;
                if (vAlign is null && table.Columns is not null && colIndex < table.Columns.Count)
                    vAlign = table.Columns[colIndex].VerticalAlignment;

                writer.WriteAttributeString("align", AlignToString(hAlign ?? TableAlignment.Left));
                writer.WriteAttributeString("valign", VAlignToString(vAlign ?? TableVerticalAlignment.Top));

                if (cell.ColSpan > 1)
                {
                    writer.WriteAttributeString("namest", $"col_{colIndex + 1}");
                    writer.WriteAttributeString("nameend", $"col_{colIndex + cell.ColSpan}");
                }

                if (cell.RowSpan > 1)
                    writer.WriteAttributeString("morerows", (cell.RowSpan - 1).ToString());

                bool isEmpty = cell.Inlines.Count == 0 && string.IsNullOrEmpty(cell.Text);
                bool hasBlockChildren = cell.Children.Any(c => c is BlockNode);

                if (isEmpty && !isHeader)
                {
                    // Empty cells: write empty string to force explicit close tag
                    writer.WriteString("");
                }
                else if (isHeader)
                {
                    if (cell.Inlines.Count > 0)
                        RenderInlines(writer, cell.Inlines, context);
                    else
                        writer.WriteString(cell.Text);
                }
                else if (hasBlockChildren)
                {
                    foreach (var cellChild in cell.Children)
                    {
                        if (cellChild is BlockNode block)
                            RenderBlock(writer, block, context);
                    }
                }
                else if (cell.Inlines.Count > 0)
                {
                    writer.WriteStartElement("para", DocBookNs);
                    WriteCellStyleOpen(writer, cell.ContentStyle);
                    RenderInlines(writer, cell.Inlines, context);
                    WriteCellStyleClose(writer, cell.ContentStyle);
                    writer.WriteEndElement(); // para
                }
                else
                {
                    writer.WriteStartElement("para", DocBookNs);
                    WriteCellStyleOpen(writer, cell.ContentStyle);
                    writer.WriteString(cell.Text);
                    WriteCellStyleClose(writer, cell.ContentStyle);
                    writer.WriteEndElement(); // para
                }

                writer.WriteEndElement(); // entry
                colIndex += cell.ColSpan;
            }
        }
        writer.WriteEndElement(); // row
    }

    private static string AlignToString(TableAlignment align) => align switch
    {
        TableAlignment.Center => "center",
        TableAlignment.Right => "right",
        _ => "left",
    };

    private static string VAlignToString(TableVerticalAlignment align) => align switch
    {
        TableVerticalAlignment.Middle => "middle",
        TableVerticalAlignment.Bottom => "bottom",
        _ => "top",
    };

    private static void WriteCellStyleOpen(XmlWriter writer, TableCellStyle style)
    {
        switch (style)
        {
            case TableCellStyle.Emphasis:
                writer.WriteStartElement("emphasis", DocBookNs);
                break;
            case TableCellStyle.Header:
                writer.WriteStartElement("emphasis", DocBookNs);
                writer.WriteAttributeString("role", "strong");
                break;
            case TableCellStyle.Monospace:
                writer.WriteStartElement("literal", DocBookNs);
                break;
            case TableCellStyle.Literal:
                writer.WriteStartElement("literal", DocBookNs);
                break;
            case TableCellStyle.Strong:
                writer.WriteStartElement("emphasis", DocBookNs);
                writer.WriteAttributeString("role", "strong");
                break;
        }
    }

    private static void WriteCellStyleClose(XmlWriter writer, TableCellStyle style)
    {
        if (style is TableCellStyle.Emphasis or TableCellStyle.Header or TableCellStyle.Monospace or TableCellStyle.Literal or TableCellStyle.Strong)
            writer.WriteEndElement();
    }

    private void RenderDelimitedBlock(XmlWriter writer, DelimitedBlockNode node, RenderContext context)
    {
        switch (node.BlockKind)
        {
            case DelimitedBlockKind.Source:
                if (node.Callouts is { Count: > 0 })
                {
                    RenderSourceBlockWithCallouts(writer, node, context);
                }
                else if (node.Language is not null)
                {
                    WriteTitledVerbatimBlock(writer, node, "programlisting",
                        w => w.WriteAttributeString("language", node.Language));
                }
                else
                {
                    WriteTitledVerbatimBlock(writer, node, "screen");
                }
                break;

            case DelimitedBlockKind.Listing:
                if (node.Callouts is { Count: > 0 })
                    RenderSourceBlockWithCallouts(writer, node, context);
                else
                    WriteTitledVerbatimBlock(writer, node, "screen");
                break;

            case DelimitedBlockKind.Literal:
                writer.WriteStartElement("literallayout", DocBookNs);
                writer.WriteAttributeString("class", "monospaced");
                writer.WriteString(node.Content ?? "");
                writer.WriteEndElement();
                break;

            case DelimitedBlockKind.Example:
                var exampleElement = node.Title is not null ? "example" : "informalexample";
                writer.WriteStartElement(exampleElement, DocBookNs);
                WriteRoles(writer, node);
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
                // Passthrough content is written as raw XML with surrounding newlines
                if (node.Content is not null)
                {
                    writer.WriteRaw("\n");
                    writer.WriteRaw(node.Content);
                    writer.WriteRaw("\n");
                }
                break;
        }
    }

    private void RenderBlockImage(XmlWriter writer, BlockImageNode node, RenderContext context)
    {
        // Asciidoctor uses <informalfigure> for images without titles, <figure> for titled images
        var elementName = node.Title is not null ? "figure" : "informalfigure";
        writer.WriteStartElement(elementName, DocBookNs);

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

        writer.WriteStartElement("bibliomisc", DocBookNs);

        // Write anchor with xreflabel (matches Asciidoctor DocBook output)
        var displayLabel = node.Label ?? node.RefId;
        writer.WriteStartElement("anchor", DocBookNs);
        writer.WriteAttributeString("xml", "id", XmlNs, node.RefId);
        writer.WriteAttributeString("xreflabel", $"[{displayLabel}]");
        writer.WriteEndElement(); // anchor

        // Write [label] prefix
        writer.WriteString($"[{displayLabel}] ");

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
                // Named footnote back-reference: use <footnoteref> (no content)
                if (n.Id is not null && n.Text is null && n.Inlines.Count == 0)
                {
                    writer.WriteStartElement("footnoteref", DocBookNs);
                    writer.WriteAttributeString("linkend", n.Id);
                    writer.WriteEndElement();
                }
                else
                {
                    writer.WriteStartElement("footnote", DocBookNs);
                    if (n.Id is not null)
                        writer.WriteAttributeString("xml", "id", XmlNs, n.Id);
                    writer.WriteStartElement("para", DocBookNs);
                    if (n.Inlines.Count > 0)
                        RenderInlines(writer, n.Inlines, context);
                    else if (n.Text is not null)
                        writer.WriteString(n.Text);
                    writer.WriteEndElement(); // para
                    writer.WriteEndElement(); // footnote
                }
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

    /// <summary>
    /// Writes a role attribute if the block node has any roles assigned.
    /// </summary>
    private static void WriteRoles(XmlWriter writer, BlockNode node)
    {
        if (node.Roles is { Count: > 0 })
            writer.WriteAttributeString("role", string.Join(" ", node.Roles));
    }

    /// <summary>
    /// Renders a source/listing block that has callout markers, emitting &lt;co&gt; elements
    /// in the programlisting and a &lt;calloutlist&gt; after it.
    /// </summary>
    private void RenderSourceBlockWithCallouts(XmlWriter writer, DelimitedBlockNode node, RenderContext context)
    {
        _calloutGroupCounter++;
        var groupId = _calloutGroupCounter;
        var callouts = node.Callouts!;

        // Build a map: lineNumber → list of callout entries for that line
        var lineCallouts = new Dictionary<int, List<CalloutEntry>>();
        foreach (var c in callouts)
        {
            if (c.LineNumber >= 0)
            {
                if (!lineCallouts.TryGetValue(c.LineNumber, out var list))
                {
                    list = [];
                    lineCallouts[c.LineNumber] = list;
                }
                list.Add(c);
            }
        }

        var elementName = node.Language is not null ? "programlisting" : "screen";

        if (node.Title is not null)
        {
            writer.WriteStartElement("formalpara", DocBookNs);
            writer.WriteElementString("title", DocBookNs, node.Title);
            writer.WriteStartElement("para", DocBookNs);
        }

        writer.WriteStartElement(elementName, DocBookNs);
        if (node.Language is not null)
            writer.WriteAttributeString("language", node.Language);
        WriteRoles(writer, node);

        // Write content line by line, inserting <co> elements where callout markers were
        var content = node.Content ?? "";
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (lineCallouts.TryGetValue(i, out var entries))
            {
                // Strip trailing comment-only text (e.g. "//") before inserting <co>
                var trimmedLine = StripTrailingCommentMarker(line);
                writer.WriteString(trimmedLine);
                foreach (var entry in entries)
                {
                    writer.WriteStartElement("co", DocBookNs);
                    writer.WriteAttributeString("xml", "id", XmlNs, $"CO{groupId}-{entry.Number}");
                    writer.WriteEndElement();
                }
            }
            else
            {
                writer.WriteString(line);
            }

            if (i < lines.Length - 1)
                writer.WriteString("\n");
        }

        writer.WriteEndElement(); // programlisting/screen

        if (node.Title is not null)
        {
            writer.WriteEndElement(); // para
            writer.WriteEndElement(); // formalpara
        }

        // Write calloutlist only if there are entries with actual explanation text
        var entriesWithText = callouts.Where(e => e.Text.Length > 0 || e.Inlines.Count > 0).ToList();
        if (entriesWithText.Count > 0)
        {
            writer.WriteStartElement("calloutlist", DocBookNs);
            foreach (var entry in entriesWithText)
            {
                writer.WriteStartElement("callout", DocBookNs);
                writer.WriteAttributeString("arearefs", $"CO{groupId}-{entry.Number}");
                writer.WriteStartElement("para", DocBookNs);
                if (entry.Inlines.Count > 0)
                    RenderInlines(writer, entry.Inlines, context);
                else
                    writer.WriteString(entry.Text);
                writer.WriteEndElement(); // para
                writer.WriteEndElement(); // callout
            }
            writer.WriteEndElement(); // calloutlist
        }
    }

    /// <summary>
    /// Strips trailing comment-only markers (e.g. "//" or "#") from a source line
    /// where callout markers were previously attached.
    /// </summary>
    private static string StripTrailingCommentMarker(string line)
    {
        var trimmed = line.TrimEnd();
        if (trimmed.EndsWith("//"))
            return trimmed[..^2];
        if (trimmed.EndsWith('#'))
            return trimmed[..^1];
        return line;
    }

    /// <summary>
    /// Writes a verbatim block (screen, programlisting, literallayout) optionally
    /// wrapped in formalpara when a title is present (matches Asciidoctor DocBook output).
    /// </summary>
    private static void WriteTitledVerbatimBlock(
        XmlWriter writer, DelimitedBlockNode node, string elementName,
        Action<XmlWriter>? writeExtraAttrs = null)
    {
        if (node.Title is not null)
        {
            writer.WriteStartElement("formalpara", DocBookNs);
            writer.WriteElementString("title", DocBookNs, node.Title);
            writer.WriteStartElement("para", DocBookNs);
        }

        writer.WriteStartElement(elementName, DocBookNs);
        writeExtraAttrs?.Invoke(writer);
        WriteRoles(writer, node);
        writer.WriteString(node.Content ?? "");
        writer.WriteEndElement();

        if (node.Title is not null)
        {
            writer.WriteEndElement(); // para
            writer.WriteEndElement(); // formalpara
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
