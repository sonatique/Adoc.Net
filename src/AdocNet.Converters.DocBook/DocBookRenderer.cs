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
        // Root element follows asciidoctor's mapping: book doctype → <book>, others → <article>.
        var isBook = context.Document.Attributes.TryGetValue("doctype", out var dt)
            && string.Equals(dt, "book", StringComparison.OrdinalIgnoreCase);
        var rootElement = isBook ? "book" : "article";
        writer.WriteStartElement(rootElement, DocBookNs);
        writer.WriteAttributeString("version", "5.0");
        // Asciidoctor binds the XLink namespace to the "xl" prefix.
        writer.WriteAttributeString("xmlns", "xl", null, XLinkNs);
        // [[anchor]] before the document title is captured as the "id" attribute and
        // becomes xml:id on the root element (Asciidoctor parity).
        if (context.Document.Attributes.TryGetValue("id", out var docId) && !string.IsNullOrWhiteSpace(docId))
            writer.WriteAttributeString("xml", "id", XmlNs, docId);
        // Asciidoctor adds xml:lang on the root, defaulting to "en". Honour the document's
        // :lang: attribute when set.
        var lang = context.Document.Attributes.TryGetValue("lang", out var l) && !string.IsNullOrWhiteSpace(l)
            ? l : "en";
        writer.WriteAttributeString("xml", "lang", XmlNs, lang);

        // Document metadata: <info><title/><subtitle/><date/><author/><authorinitials/>
        // <revhistory/></info> wrapper, matching Asciidoctor's standalone DocBook output.
        // Date precedence: :revdate: → :docdate: → omit.
        // :reproducible: opt-out: when set, the date is suppressed entirely.
        if (context.Document.Title is not null)
        {
            writer.WriteStartElement("info", DocBookNs);
            // Asciidoctor splits a "Title: Subtitle" header into <title> + <subtitle>.
            var (titleText, subtitleText) = SplitTitleSubtitle(context.Document.Title);
            writer.WriteElementString("title", DocBookNs, titleText);
            if (subtitleText is not null)
                writer.WriteElementString("subtitle", DocBookNs, subtitleText);

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

            WriteAuthorElements(writer, attrs);
            WriteRevhistory(writer, attrs);

            writer.WriteEndElement(); // info
        }

        // Render children with section nesting:
        // DocBook requires sections to be nested (level-2 inside level-1, etc.)
        // while the AST stores them as flat siblings. Build the nesting here.
        RenderChildrenWithSectionNesting(writer, context.Document.Children, context);

        writer.WriteEndElement(); // root (article or book)
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
        // Discrete headings render as <bridgehead renderas="sectN"> rather than a
        // wrapping section element. They produce no nesting and consume no siblings.
        if (node.IsDiscrete)
        {
            writer.WriteStartElement("bridgehead", DocBookNs);
            writer.WriteAttributeString("renderas", $"sect{Math.Max(1, node.Level)}");
            if (node.Id is not null)
                writer.WriteAttributeString("xml", "id", XmlNs, node.Id);
            if (node.TitleInlines.Count > 0)
                RenderInlines(writer, node.TitleInlines, context);
            else
                writer.WriteString(node.Title);
            writer.WriteEndElement();
            index++;
            return;
        }

        // Detect bibliography sections (sections containing BibliographyEntryNode children)
        bool isBibliography = node.Children.Any(c => c is BibliographyEntryNode);

        // Asciidoctor section element mapping:
        //   - [appendix]  → <appendix>
        //   - book doctype, level 1 → <chapter>
        //   - bibliography children → <bibliography>
        //   - everything else → <section>
        bool isBook = context.Document.Attributes.TryGetValue("doctype", out var dt)
            && string.Equals(dt, "book", StringComparison.OrdinalIgnoreCase);
        string sectionElement;
        if (isBibliography)
            sectionElement = "bibliography";
        else if (string.Equals(node.Style, "appendix", StringComparison.OrdinalIgnoreCase))
            sectionElement = "appendix";
        else if (isBook && node.Level == 1)
            sectionElement = "chapter";
        else
            sectionElement = "section";
        writer.WriteStartElement(sectionElement, DocBookNs);

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

        writer.WriteEndElement(); // section/chapter/appendix/bibliography
    }

    private void RenderSection(XmlWriter writer, SectionNode node, RenderContext context)
    {
        // Discrete sections render as <bridgehead> with no nesting (asciidoctor parity).
        if (node.IsDiscrete)
        {
            writer.WriteStartElement("bridgehead", DocBookNs);
            writer.WriteAttributeString("renderas", $"sect{Math.Max(1, node.Level)}");
            if (node.Id is not null)
                writer.WriteAttributeString("xml", "id", XmlNs, node.Id);
            if (node.TitleInlines.Count > 0)
                RenderInlines(writer, node.TitleInlines, context);
            else
                writer.WriteString(node.Title);
            writer.WriteEndElement();
            return;
        }

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

        // Asciidoctor adds mark="none" on <itemizedlist> when the list contains
        // checklist items ([x] / [ ] markers), suppressing the default bullet
        // since the rendered ✓ / ❏ glyph takes its place.
        if (node.ListKind == ListKind.Unordered)
        {
            bool isChecklist = false;
            foreach (var child in node.Children)
            {
                if (child is ListItemNode li && li.Checked is not null)
                {
                    isChecklist = true;
                    break;
                }
            }
            if (isChecklist)
                writer.WriteAttributeString("mark", "none");
        }

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
            // Checklist items get a leading ✓ (checked) or ❏ (unchecked) glyph
            // followed by a space — matching asciidoctor's DocBook output.
            if (node.Checked is not null)
                writer.WriteString(node.Checked.Value ? "\u2713 " : "\u274F ");
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

        // Asciidoctor always emits frame, rowsep, colsep on the table element.
        // Defaults: frame="all", rowsep="1", colsep="1" (full grid).
        writer.WriteAttributeString("frame", "all");
        writer.WriteAttributeString("rowsep", "1");
        writer.WriteAttributeString("colsep", "1");

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
        // Asciidoctor scales colspec widths so they sum to 100 (effective
        // percentage). E.g. [cols="2"] (two equal cols) produces colwidth="50*"
        // each; [cols="1,2,1"] produces "25*", "50*", "25*". Compute by summing
        // the parsed proportional widths and rescaling.
        if (node.Columns is not null)
        {
            int totalWidth = 0;
            for (int i = 0; i < node.Columns.Count; i++)
                totalWidth += node.Columns[i].Width;
            if (totalWidth <= 0) totalWidth = node.Columns.Count;

            // Asciidoctor sums to ~100 by:
            //   1. Truncating each column's percentage to 4 decimals
            //   2. Adding the rounding residual onto the LAST column so the
            //      total comes out as exactly 100.0001 (not 99.9999)
            // We mirror that rather than letting Math.Round drift.
            var widths = new double[node.Columns.Count];
            double sum = 0;
            for (int i = 0; i < node.Columns.Count; i++)
            {
                double exact = (node.Columns[i].Width * 100.0) / totalWidth;
                // Truncate to 4 decimal places (asciidoctor's effective precision)
                widths[i] = Math.Truncate(exact * 10000) / 10000;
                sum += widths[i];
            }
            // Asciidoctor's last-column rule: width[last] = 100 - sum(others)
            // when truncation shaved off something, capped at 4-decimal
            // precision. This produces the exact "50.0001*" form rather than
            // the 0.0002 residual our naive sum would give. When the columns
            // already sum to exactly 100, no adjustment is needed.
            if (widths.Length > 0 && sum < 100)
            {
                double prefixSum = 0;
                for (int i = 0; i < widths.Length - 1; i++)
                    prefixSum += widths[i];
                double lastTarget = 100 - prefixSum;
                widths[^1] = Math.Truncate(lastTarget * 10000) / 10000;
                // Ensure minimum 0.0001 over 100 — matches asciidoctor's tendency
                // to round just past 100 rather than just under.
                double newSum = prefixSum + widths[^1];
                if (newSum < 100)
                    widths[^1] = Math.Truncate((widths[^1] + (100.0001 - newSum)) * 10000) / 10000;
            }
            for (int i = 0; i < node.Columns.Count; i++)
            {
                writer.WriteStartElement("colspec", DocBookNs);
                writer.WriteAttributeString("colname", $"col_{i + 1}");
                writer.WriteAttributeString("colwidth", FormatColspecWidth(widths[i]));
                writer.WriteEndElement(); // colspec
            }
        }
        else
        {
            double defaultWidth = colCount > 0 ? 100.0 / colCount : 1;
            for (var i = 0; i < colCount; i++)
            {
                writer.WriteStartElement("colspec", DocBookNs);
                writer.WriteAttributeString("colname", $"col_{i + 1}");
                writer.WriteAttributeString("colwidth", FormatColspecWidth(defaultWidth));
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
                    // Asciidoctor uses <simpara> for inline-only cell content;
                    // <para> is reserved for cells with nested block content.
                    writer.WriteStartElement("simpara", DocBookNs);
                    WriteCellStyleOpen(writer, cell.ContentStyle);
                    RenderInlines(writer, cell.Inlines, context);
                    WriteCellStyleClose(writer, cell.ContentStyle);
                    writer.WriteEndElement(); // simpara
                }
                else
                {
                    writer.WriteStartElement("simpara", DocBookNs);
                    WriteCellStyleOpen(writer, cell.ContentStyle);
                    writer.WriteString(cell.Text);
                    WriteCellStyleClose(writer, cell.ContentStyle);
                    writer.WriteEndElement(); // simpara
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
                    // Asciidoctor emits linenumbering="unnumbered" on the
                    // <screen> for a source block with no language.
                    WriteTitledVerbatimBlock(writer, node, "screen",
                        w => w.WriteAttributeString("linenumbering", "unnumbered"));
                }
                break;

            case DelimitedBlockKind.Listing:
                if (node.Callouts is { Count: > 0 })
                    RenderSourceBlockWithCallouts(writer, node, context);
                else
                    // Asciidoctor produces bare <screen> for ---- listing blocks
                    // (in contrast to ``` fenced source blocks which add linenumbering).
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
                if (node.Id is not null)
                    writer.WriteAttributeString("xml", "id", XmlNs, node.Id);
                WriteRoles(writer, node);
                if (node.Title is not null)
                {
                    writer.WriteStartElement("title", DocBookNs);
                    RenderLabelInlines(writer, node.Title, context);
                    writer.WriteEndElement();
                }
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
                // Quote body lives on node.Content (raw text between delimiters)
                // when there are no nested blocks; emit it as a <simpara>.
                if (!string.IsNullOrEmpty(node.Content))
                {
                    writer.WriteStartElement("simpara", DocBookNs);
                    var quoteInlines = AdocNet.Parser.InlineParser.Parse(
                        node.Content!, node.Substitutions ?? SubstitutionKind.Normal,
                        context.Document.Attributes);
                    RenderInlines(writer, quoteInlines, context);
                    writer.WriteEndElement(); // simpara
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
                if (node.Id is not null)
                    writer.WriteAttributeString("xml", "id", XmlNs, node.Id);
                if (node.Title is not null)
                {
                    writer.WriteStartElement("title", DocBookNs);
                    RenderLabelInlines(writer, node.Title, context);
                    writer.WriteEndElement();
                }
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
        // Asciidoctor maps the image align attribute to imagedata/@align.
        // Our parser stores it as a "text-{align}" role; reverse the mapping here.
        foreach (var role in node.Roles)
        {
            if (role.StartsWith("text-", StringComparison.Ordinal))
            {
                writer.WriteAttributeString("align", role.Substring(5));
                break;
            }
        }
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

        // Block titles set via .Title above the admonition appear as <title> child.
        if (!string.IsNullOrEmpty(node.Title))
        {
            writer.WriteStartElement("title", DocBookNs);
            var titleInlines = AdocNet.Parser.InlineParser.Parse(
                node.Title!, SubstitutionKind.Normal, context.Document.Attributes);
            RenderInlines(writer, titleInlines, context);
            writer.WriteEndElement();
        }

        // Asciidoctor uses <simpara> (inline-only paragraph) for admonition text;
        // <para> is reserved for paragraphs with nested block content. The admonition
        // text itself is always inline-only, so <simpara> is correct.
        if (node.Inlines.Count > 0)
        {
            writer.WriteStartElement("simpara", DocBookNs);
            RenderInlines(writer, node.Inlines, context);
            writer.WriteEndElement(); // simpara
        }
        else if (node.Text is not null)
        {
            writer.WriteStartElement("simpara", DocBookNs);
            writer.WriteString(node.Text);
            writer.WriteEndElement(); // simpara
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
        // [qanda] description lists render as <qandaset> in DocBook (asciidoctor parity).
        // [horizontal] description lists render as a 2-column <informaltable>.
        bool isQandA = string.Equals(node.Style, "qanda", StringComparison.OrdinalIgnoreCase);
        bool isHorizontal = string.Equals(node.Style, "horizontal", StringComparison.OrdinalIgnoreCase);

        if (isHorizontal)
        {
            RenderHorizontalDescriptionList(writer, node, context);
            return;
        }

        writer.WriteStartElement(isQandA ? "qandaset" : "variablelist", DocBookNs);

        foreach (var child in node.Children)
        {
            if (child is DescriptionItemNode item)
            {
                if (isQandA)
                    RenderQandAEntry(writer, item, context);
                else
                    RenderDescriptionItem(writer, item, context);
            }
        }

        writer.WriteEndElement(); // qandaset/variablelist
    }

    private void RenderHorizontalDescriptionList(XmlWriter writer, DescriptionListNode node, RenderContext context)
    {
        writer.WriteStartElement("informaltable", DocBookNs);
        writer.WriteAttributeString("colsep", "0");
        writer.WriteAttributeString("frame", "none");
        writer.WriteAttributeString("rowsep", "0");
        writer.WriteAttributeString("tabstyle", "horizontal");

        writer.WriteStartElement("tgroup", DocBookNs);
        writer.WriteAttributeString("cols", "2");

        writer.WriteStartElement("colspec", DocBookNs);
        writer.WriteAttributeString("colwidth", "15*");
        writer.WriteEndElement();
        writer.WriteStartElement("colspec", DocBookNs);
        writer.WriteAttributeString("colwidth", "85*");
        writer.WriteEndElement();

        writer.WriteStartElement("tbody", DocBookNs);
        writer.WriteAttributeString("valign", "top");

        foreach (var child in node.Children)
        {
            if (child is not DescriptionItemNode item) continue;
            writer.WriteStartElement("row", DocBookNs);

            // term cell
            writer.WriteStartElement("entry", DocBookNs);
            writer.WriteStartElement("simpara", DocBookNs);
            if (item.TermInlines.Count > 0)
                RenderInlines(writer, item.TermInlines, context);
            else
                writer.WriteString(item.Terms[0]);
            writer.WriteEndElement(); // simpara
            writer.WriteEndElement(); // entry

            // description cell
            writer.WriteStartElement("entry", DocBookNs);
            writer.WriteStartElement("simpara", DocBookNs);
            if (item.DescriptionInlines.Count > 0)
                RenderInlines(writer, item.DescriptionInlines, context);
            else
                writer.WriteString(item.Description);
            writer.WriteEndElement(); // simpara
            writer.WriteEndElement(); // entry

            writer.WriteEndElement(); // row
        }

        writer.WriteEndElement(); // tbody
        writer.WriteEndElement(); // tgroup
        writer.WriteEndElement(); // informaltable
    }

    private void RenderQandAEntry(XmlWriter writer, DescriptionItemNode node, RenderContext context)
    {
        writer.WriteStartElement("qandaentry", DocBookNs);

        writer.WriteStartElement("question", DocBookNs);
        writer.WriteStartElement("simpara", DocBookNs);
        if (node.TermInlines.Count > 0)
            RenderInlines(writer, node.TermInlines, context);
        else
            writer.WriteString(node.Terms[0]);
        writer.WriteEndElement(); // simpara
        writer.WriteEndElement(); // question

        writer.WriteStartElement("answer", DocBookNs);
        writer.WriteStartElement("simpara", DocBookNs);
        if (node.DescriptionInlines.Count > 0)
            RenderInlines(writer, node.DescriptionInlines, context);
        else
            writer.WriteString(node.Description);
        writer.WriteEndElement(); // simpara
        foreach (var child in node.Children)
        {
            if (child is BlockNode block)
                RenderBlock(writer, block, context);
        }
        writer.WriteEndElement(); // answer

        writer.WriteEndElement(); // qandaentry
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
        // Asciidoctor uses <simpara> for inline-only description content; <para>
        // is reserved for descriptions with nested block content. Skip the
        // <simpara> entirely when the description is empty (asciidoctor parity).
        bool hasInlineDescription =
            node.DescriptionInlines.Count > 0
            || !string.IsNullOrEmpty(node.Description);
        if (hasInlineDescription)
        {
            writer.WriteStartElement("simpara", DocBookNs);
            if (node.DescriptionInlines.Count > 0)
                RenderInlines(writer, node.DescriptionInlines, context);
            else
                writer.WriteString(node.Description);
            writer.WriteEndElement(); // simpara
        }

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
        bool first = true;
        foreach (var node in nodes)
        {
            if (first)
            {
                // Force the parent element into mixed-content mode so XmlWriter
                // stops pretty-printing (indenting) inline child elements.
                // Asciidoctor keeps inline content on a single line — e.g. a lone
                // <link> in a <simpara> stays <simpara><link…>…</link></simpara>,
                // not split across indented lines. Guarded by `first` so an empty
                // inline list leaves the element self-closing.
                writer.WriteRaw("");
                first = false;
            }
            RenderInline(writer, node, context);
        }
    }

    private void RenderInline(XmlWriter writer, InlineNode node, RenderContext context)
    {
        switch (node)
        {
            case TextInlineNode n:
                WriteTextWithQuoteWrap(writer, n.Value);
                break;

            case StrongInlineNode n:
                writer.WriteStartElement("emphasis", DocBookNs);
                writer.WriteAttributeString("role", "strong");
                // [.role]*text* / *[.role]text*: asciidoctor wraps inner text in
                // <phrase role="..."> when roles are present (e.g. [.term]).
                RenderInlinesMaybeWrappedInPhrase(writer, n.Children, n.Roles, context);
                writer.WriteEndElement();
                break;

            case EmphasisInlineNode n:
                writer.WriteStartElement("emphasis", DocBookNs);
                RenderInlinesMaybeWrappedInPhrase(writer, n.Children, n.Roles, context);
                writer.WriteEndElement();
                break;

            case MonospaceInlineNode n:
                writer.WriteStartElement("literal", DocBookNs);
                RenderInlines(writer, n.Children, context);
                writer.WriteEndElement();
                break;

            case LinkInlineNode n:
                writer.WriteStartElement("link", DocBookNs);
                writer.WriteAttributeString("xl", "href", XLinkNs, n.Url);
                writer.WriteString(MaybeHideUriScheme(n.Url, context));
                writer.WriteEndElement();
                break;

            case InlineLinkMacroNode n:
                writer.WriteStartElement("link", DocBookNs);
                writer.WriteAttributeString("xl", "href", XLinkNs, n.Url);
                if (n.Label.Length > 0)
                    RenderLabelInlines(writer, n.Label, context);
                else
                    writer.WriteString(MaybeHideUriScheme(n.Url, context));
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
                    // Asciidoctor uses <simpara> for inline-only footnote text;
                    // <para> is for footnotes with nested block content.
                    writer.WriteStartElement("simpara", DocBookNs);
                    if (n.Inlines.Count > 0)
                        RenderInlines(writer, n.Inlines, context);
                    else if (n.Text is not null)
                        writer.WriteString(n.Text);
                    writer.WriteEndElement(); // simpara
                    writer.WriteEndElement(); // footnote
                }
                break;

            case CrossReferenceInlineNode n:
                if (n.Label is not null)
                {
                    writer.WriteStartElement("link", DocBookNs);
                    writer.WriteAttributeString("linkend", n.Target);
                    RenderLabelInlines(writer, n.Label, context);
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
                // Asciidoctor renders inter-document xrefs as <link xl:href="...">
                // (NOT <olink>), with the .adoc extension replaced by .xml. olink is
                // a less-portable DocBook construct that requires a target database.
                // When no explicit label is given, the label defaults to the .xml
                // path (matches asciidoctor — the rendered DocBook target is .xml).
                writer.WriteStartElement("link", DocBookNs);
                var xmlPath = ConvertAdocExtensionToXml(n.Path);
                var href = n.Id is not null ? xmlPath + "#" + n.Id : xmlPath;
                writer.WriteAttributeString("xl", "href", XLinkNs, href);
                // Parse the label as inlines so backticks become <literal>, etc.
                // RenderLabelInlines includes the smart-punctuation/replacement passes.
                if (n.Label is not null)
                    RenderLabelInlines(writer, n.Label, context);
                else
                    writer.WriteString(xmlPath);
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
    /// Formats a colspec width to match asciidoctor's "N.NNNN*" output.
    /// Whole-number widths render as "N*" with no decimal portion.
    /// Fractional widths render with up to 4 decimal places (trailing zeros stripped).
    /// </summary>
    private static string FormatColspecWidth(double width)
    {
        if (width == Math.Floor(width))
            return $"{(int)width}*";
        // Use invariant culture so the decimal separator is always '.'
        return string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0:0.####}*", width);
    }

    /// <summary>
    /// Splits a document title at the first ": " into a title and subtitle pair.
    /// Asciidoctor uses this convention to populate &lt;subtitle&gt; in DocBook.
    /// Returns (title, null) when there's no separator.
    /// </summary>
    private static (string Title, string? Subtitle) SplitTitleSubtitle(string fullTitle)
    {
        var idx = fullTitle.IndexOf(": ", StringComparison.Ordinal);
        if (idx < 0) return (fullTitle, null);
        return (fullTitle.Substring(0, idx), fullTitle.Substring(idx + 2));
    }

    /// <summary>
    /// Emits &lt;author&gt;...&lt;/author&gt; and &lt;authorinitials&gt; elements derived
    /// from the :author: / :email: / :authorinitials: document attributes.
    /// Mirrors asciidoctor's DocBook author block.
    /// </summary>
    private static void WriteAuthorElements(XmlWriter writer, IReadOnlyDictionary<string, string> attrs)
    {
        if (!attrs.TryGetValue("author", out var author) || string.IsNullOrWhiteSpace(author))
            return;

        writer.WriteStartElement("author", DocBookNs);
        writer.WriteStartElement("personname", DocBookNs);

        // Asciidoctor parses author as "First [Middle] Last" — split into firstname/
        // optional othername/surname. Single-word author becomes just <firstname>.
        var parts = author.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            writer.WriteElementString("firstname", DocBookNs, parts[0]);
        }
        else if (parts.Length == 2)
        {
            writer.WriteElementString("firstname", DocBookNs, parts[0]);
            writer.WriteElementString("surname", DocBookNs, parts[1]);
        }
        else
        {
            writer.WriteElementString("firstname", DocBookNs, parts[0]);
            for (int i = 1; i < parts.Length - 1; i++)
                writer.WriteElementString("othername", DocBookNs, parts[i]);
            writer.WriteElementString("surname", DocBookNs, parts[^1]);
        }
        writer.WriteEndElement(); // personname

        if (attrs.TryGetValue("email", out var email) && !string.IsNullOrWhiteSpace(email))
            writer.WriteElementString("email", DocBookNs, email);

        writer.WriteEndElement(); // author

        // :authorinitials: is set explicitly by some docs but most rely on
        // asciidoctor's auto-derivation from the author name (first letter of
        // each part). We replicate that fallback so output matches.
        var initials = ResolveAuthorInitials(attrs, parts);
        if (!string.IsNullOrEmpty(initials))
            writer.WriteElementString("authorinitials", DocBookNs, initials);
    }

    private static string? ResolveAuthorInitials(IReadOnlyDictionary<string, string> attrs, string[] authorParts)
    {
        if (attrs.TryGetValue("authorinitials", out var explicitInitials) && !string.IsNullOrWhiteSpace(explicitInitials))
            return explicitInitials;
        if (authorParts.Length == 0) return null;
        var sb = new System.Text.StringBuilder(authorParts.Length);
        foreach (var p in authorParts)
        {
            if (p.Length > 0) sb.Append(char.ToUpperInvariant(p[0]));
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>
    /// Emits the &lt;revhistory&gt;&lt;revision&gt; block when :revnumber: is set.
    /// </summary>
    private static void WriteRevhistory(XmlWriter writer, IReadOnlyDictionary<string, string> attrs)
    {
        if (!attrs.TryGetValue("revnumber", out var revnumber) || string.IsNullOrWhiteSpace(revnumber))
            return;
        // Asciidoctor only emits <revhistory> when there is also a :revdate: or
        // :revremark: alongside the revnumber. Bare revnumber alone produces no
        // revhistory in the DocBook output.
        bool hasDate = attrs.TryGetValue("revdate", out var rd) && !string.IsNullOrWhiteSpace(rd);
        bool hasRemark = attrs.TryGetValue("revremark", out var rr) && !string.IsNullOrWhiteSpace(rr);
        if (!hasDate && !hasRemark)
            return;
        writer.WriteStartElement("revhistory", DocBookNs);
        writer.WriteStartElement("revision", DocBookNs);
        writer.WriteElementString("revnumber", DocBookNs, revnumber);
        if (attrs.TryGetValue("revdate", out var revdate) && !string.IsNullOrWhiteSpace(revdate))
            writer.WriteElementString("date", DocBookNs, revdate);
        // Compute the same auto-derived initials used in <author>; falls back to explicit attr.
        var authorParts = attrs.TryGetValue("author", out var author) && !string.IsNullOrWhiteSpace(author)
            ? author.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();
        var initials = ResolveAuthorInitials(attrs, authorParts);
        if (!string.IsNullOrEmpty(initials))
            writer.WriteElementString("authorinitials", DocBookNs, initials);
        if (attrs.TryGetValue("revremark", out var remark) && !string.IsNullOrWhiteSpace(remark))
            writer.WriteElementString("revremark", DocBookNs, remark);
        writer.WriteEndElement(); // revision
        writer.WriteEndElement(); // revhistory
    }

    /// <summary>
    /// Writes text to the DocBook stream, wrapping any "...''" curly-double-quoted
    /// span in &lt;quote&gt;...&lt;/quote&gt; (asciidoctor parity — backend-aware
    /// substitution of straight double quotes into the DocBook quote element).
    /// Idempotent for text without curly quotes.
    /// </summary>
    private static void WriteTextWithQuoteWrap(XmlWriter writer, string text)
    {
        // Fast path: no left-curly-quote means no work to do.
        if (text.IndexOf('\u201C') < 0)
        {
            writer.WriteString(text);
            return;
        }
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf('\u201C', i);
            if (open < 0)
            {
                writer.WriteString(text.Substring(i));
                return;
            }
            int close = text.IndexOf('\u201D', open + 1);
            if (close < 0)
            {
                writer.WriteString(text.Substring(i));
                return;
            }
            // Emit any prefix text before the opening curly quote
            if (open > i)
                writer.WriteString(text.Substring(i, open - i));
            // Emit <quote>inner</quote> for the span between curly quotes
            writer.WriteStartElement("quote", DocBookNs);
            writer.WriteString(text.Substring(open + 1, close - open - 1));
            writer.WriteEndElement();
            i = close + 1;
        }
    }

    /// <summary>
    /// Renders a string label (link/xref text) as parsed inlines. Backticks become
    /// &lt;literal&gt;, *text* becomes &lt;emphasis role="strong"&gt;, etc. Matches Asciidoctor
    /// which applies the full text-substitution pipeline (minus Macros, to avoid
    /// re-entering link parsing) to link labels.
    /// </summary>
    private void RenderLabelInlines(XmlWriter writer, string label, RenderContext context)
    {
        var subs = SubstitutionKind.Quotes |
                   SubstitutionKind.Replacements |
                   SubstitutionKind.PostReplacements;
        var inlines = AdocNet.Parser.InlineParser.Parse(label, subs, context.Document.Attributes);
        RenderInlines(writer, inlines, context);
    }

    /// <summary>
    /// Strips the URI scheme prefix (http://, https://, mailto:) when the
    /// :hide-uri-scheme: attribute is set on the document — matches asciidoctor's
    /// behaviour for bare URLs and link macros without an explicit label.
    /// </summary>
    private static string MaybeHideUriScheme(string url, RenderContext context)
    {
        if (!context.Document.Attributes.ContainsKey("hide-uri-scheme"))
            return url;
        foreach (var prefix in new[] { "https://", "http://", "ftp://", "mailto:", "irc://" })
        {
            if (url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return url[prefix.Length..];
        }
        return url;
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

        // Pre-scan whether any line actually has a callout marker. Asciidoctor only
        // emits linenumbering="unnumbered" and fills <callout arearefs="…"> when the
        // source content has real callout markers. Without them (e.g. include macro
        // stubbed for conformance testing), we still emit the calloutlist but with
        // empty arearefs and no linenumbering attribute.
        var content = node.Content ?? "";
        var lines = content.Split('\n');
        bool hasCalloutMarkers = false;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lineCallouts.ContainsKey(i)) { hasCalloutMarkers = true; break; }
        }

        if (node.Title is not null)
        {
            writer.WriteStartElement("formalpara", DocBookNs);
            // Asciidoctor propagates the block's id to the formalpara wrapper.
            if (node.Id is not null)
                writer.WriteAttributeString("xml", "id", XmlNs, node.Id);
            writer.WriteElementString("title", DocBookNs, node.Title);
            writer.WriteStartElement("para", DocBookNs);
        }

        writer.WriteStartElement(elementName, DocBookNs);
        if (node.Language is not null)
            writer.WriteAttributeString("language", node.Language);
        // linenumbering only when language is set OR actual <co> markers will be emitted.
        if (node.Language is not null || hasCalloutMarkers)
            writer.WriteAttributeString("linenumbering", "unnumbered");
        WriteRoles(writer, node);

        // Write content line by line, inserting <co> elements where callout markers were
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
                // arearefs only refers to <co> ids that were actually emitted in the
                // listing. When the source has no callout markers (e.g. include macro
                // was stripped), arearefs stays empty — Asciidoctor's behaviour.
                writer.WriteAttributeString("arearefs",
                    hasCalloutMarkers ? $"CO{groupId}-{entry.Number}" : "");
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
            // Propagate the block's id and roles to the formalpara wrapper
            // (asciidoctor parity — when wrapped, role attaches to <formalpara>,
            // not the inner programlisting/screen).
            if (node.Id is not null)
                writer.WriteAttributeString("xml", "id", XmlNs, node.Id);
            WriteRoles(writer, node);
            writer.WriteElementString("title", DocBookNs, node.Title);
            writer.WriteStartElement("para", DocBookNs);
        }

        writer.WriteStartElement(elementName, DocBookNs);
        writeExtraAttrs?.Invoke(writer);
        // Asciidoctor adds linenumbering="unnumbered" on programlisting; for
        // screen blocks the caller is responsible (passes it via writeExtraAttrs)
        // because some <screen> origins (e.g. literal block via [literal]) should NOT have it.
        if (elementName == "programlisting")
            writer.WriteAttributeString("linenumbering", "unnumbered");
        // When unwrapped (no title), roles attach to the inner element directly.
        if (node.Title is null)
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
            {
                // Multi-key chords (e.g. kbd:[Ctrl+Shift+P]) split at "+" into a
                // <keycombo> with one <keycap> per key. Single keys emit a bare
                // <keycap>.
                var keys = node.Content.Split('+');
                if (keys.Length > 1)
                {
                    writer.WriteStartElement("keycombo", DocBookNs);
                    foreach (var k in keys)
                        writer.WriteElementString("keycap", DocBookNs, k.Trim());
                    writer.WriteEndElement(); // keycombo
                }
                else
                {
                    writer.WriteStartElement("keycap", DocBookNs);
                    writer.WriteString(node.Content);
                    writer.WriteEndElement();
                }
                break;
            }

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

    /// <summary>
    /// Renders inline children, optionally wrapping them in a &lt;phrase role="..."&gt;
    /// element when one or more roles are set on the parent (asciidoctor parity for
    /// [.role]*text* and similar role-decorated formatting markers).
    /// </summary>
    private void RenderInlinesMaybeWrappedInPhrase(
        XmlWriter writer, IReadOnlyList<InlineNode> children,
        IReadOnlyList<string>? roles, RenderContext context)
    {
        if (roles is { Count: > 0 })
        {
            writer.WriteStartElement("phrase", DocBookNs);
            writer.WriteAttributeString("role", string.Join(" ", roles));
            RenderInlines(writer, children, context);
            writer.WriteEndElement(); // phrase
        }
        else
        {
            RenderInlines(writer, children, context);
        }
    }

    /// <summary>
    /// Replaces a trailing `.adoc` extension with `.xml` for inter-document
    /// xref hrefs (asciidoctor convention: source is .adoc, rendered DocBook
    /// target is .xml). Leaves the path unchanged if there's no .adoc suffix.
    /// </summary>
    private static string ConvertAdocExtensionToXml(string path)
    {
        if (path.EndsWith(".adoc", StringComparison.Ordinal))
            return path[..^5] + ".xml";
        return path;
    }
}
