using System.Text;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Converters.Epub;

/// <summary>
/// Renders a chapter body to the semantic-HTML5 structure asciidoctor-epub3
/// uses inside EPUB chapter XHTML files. Distinct from HtmlRenderer which
/// emits a div-wrapped structure suited for standalone HTML pages.
///
/// Key differences from HtmlRenderer:
/// <list type="bullet">
///   <item>Sections wrap in <c>&lt;section class="sect{N}" title="…"&gt;</c>
///   with the heading at level N (not N+1).</item>
///   <item>Paragraphs are bare <c>&lt;p&gt;</c> elements, no
///   <c>&lt;div class="paragraph"&gt;</c> wrapper.</item>
///   <item>Admonitions use <c>&lt;aside class="admonition note"&gt;</c>
///   (semantic HTML5) rather than the table-based wrapper.</item>
///   <item>Source blocks use <c>&lt;figure class="listing"&gt;</c>.</item>
///   <item>Lists wrap items in <c>&lt;span class="principal"&gt;</c>.</item>
///   <item>Inline strong/emphasis use <c>&lt;b&gt;/&lt;i&gt;</c> (not
///   <c>&lt;strong&gt;/&lt;em&gt;</c>); monospace is
///   <c>&lt;code class="literal"&gt;</c>; links carry <c>class="link"</c>.</item>
/// </list>
/// Matches the output of asciidoctor-epub3 so that the bundled epub3.css
/// rules apply cleanly and the chapter XHTML diff against asciidoctor's
/// reference output stays minimal.
/// </summary>
internal sealed class EpubChapterRenderer
{
    private readonly DocumentNode _document;
    private readonly bool _sectnumsEnabled;
    private readonly int[] _sectionCounters = new int[6];
    private int _exampleCounter;
    private int _listingCounter;
    private int _tableCounter;
    private int _figureCounter;

    public EpubChapterRenderer(DocumentNode document)
    {
        _document = document;
        _sectnumsEnabled = document.Attributes.ContainsKey("sectnums");
    }

    /// <summary>Renders every child of the given container as chapter body content.</summary>
    public string RenderBody(IEnumerable<AstNode> children)
    {
        var blocks = children.OfType<BlockNode>().ToList();
        // Find the absolute-last ParagraphNode in the chapter (deep walk through
        // sections). Asciidoctor-epub3 only adds class="last" to that one
        // paragraph — not to last-in-container at every level.
        _lastParagraphInChapter = FindAbsoluteLastParagraph(blocks);
        var sb = new StringBuilder();
        foreach (var b in blocks)
            RenderBlock(sb, b, isLastInContainer: false);
        return sb.ToString();
    }

    private ParagraphNode? _lastParagraphInChapter;

    private static ParagraphNode? FindAbsoluteLastParagraph(IReadOnlyList<BlockNode> blocks)
    {
        // Walk the tree from the end backwards looking for the last ParagraphNode.
        for (int i = blocks.Count - 1; i >= 0; i--)
        {
            var found = FindLastParagraphIn(blocks[i]);
            if (found is not null) return found;
        }
        return null;
    }

    private static ParagraphNode? FindLastParagraphIn(AstNode node)
    {
        if (node is ParagraphNode p) return p;
        // Recurse into descendants in reverse to find the absolute-last paragraph.
        var children = node.Children.OfType<AstNode>().ToList();
        for (int i = children.Count - 1; i >= 0; i--)
        {
            var found = FindLastParagraphIn(children[i]);
            if (found is not null) return found;
        }
        return null;
    }

    // ── Block dispatch ───────────────────────────────────────────────────

    private void RenderBlock(StringBuilder sb, BlockNode node, bool isLastInContainer)
    {
        switch (node)
        {
            case SectionNode n: RenderSection(sb, n); break;
            case ParagraphNode n: RenderParagraph(sb, n, isLastInContainer); break;
            case ListNode n: RenderList(sb, n); break;
            case DescriptionListNode n: RenderDescriptionList(sb, n); break;
            case DelimitedBlockNode n: RenderDelimitedBlock(sb, n); break;
            case AdmonitionNode n: RenderAdmonition(sb, n); break;
            case BlockImageNode n: RenderImage(sb, n); break;
            case TableNode n: RenderTable(sb, n); break;
            case StemBlockNode n: RenderStem(sb, n); break;
            case ThematicBreakNode: sb.Append("<hr class=\"thematicbreak\"/>\n"); break;
            case PageBreakNode: sb.Append("<hr class=\"pagebreak\"/>\n"); break;
            default: break;
        }
    }

    // ── Sections ─────────────────────────────────────────────────────────

    private void RenderSection(SectionNode section)
    {
        // Discrete headings are inline, not section-wrapping.
        var sb = new StringBuilder();
        RenderSectionInto(sb, section);
    }

    private void RenderSection(StringBuilder sb, SectionNode section)
    {
        if (section.IsDiscrete)
        {
            var dtag = section.Level switch { 1 => "h2", 2 => "h3", 3 => "h4", 4 => "h5", _ => "h6" };
            sb.Append('<').Append(dtag).Append(" class=\"discrete\"");
            if (section.Id is not null) sb.Append(" id=\"").Append(EscapeXmlAttr(section.Id)).Append('"');
            sb.Append('>');
            RenderInlines(sb, GetTitleInlines(section));
            sb.Append("</").Append(dtag).Append(">\n");
            return;
        }

        RenderSectionInto(sb, section);
    }

    private void RenderSectionInto(StringBuilder sb, SectionNode section)
    {
        int level = section.Level;
        // Compute the numbered title (e.g. "1. Foo" or "1.2. Bar") when :sectnums:
        // is enabled. Counter array advances at the section's level; deeper counters
        // reset.
        string numberPrefix = "";
        if (_sectnumsEnabled && !section.IsDiscrete && level >= 1 && level <= _sectionCounters.Length)
        {
            int idx = level - 1;
            _sectionCounters[idx]++;
            for (int i = idx + 1; i < _sectionCounters.Length; i++) _sectionCounters[i] = 0;
            var pb = new StringBuilder();
            for (int i = 0; i <= idx; i++) { pb.Append(_sectionCounters[i]); pb.Append('.'); }
            pb.Append(' ');
            numberPrefix = pb.ToString();
        }
        // Appendix style overrides numeric prefix with letter prefix.
        if (string.Equals(section.Style, "appendix", StringComparison.OrdinalIgnoreCase))
        {
            numberPrefix = "Appendix A: "; // simple — Appendix counter would need state
        }

        var titleInlines = GetTitleInlines(section);
        var titleText = numberPrefix + InlinesPlainText(titleInlines);

        sb.Append("<section class=\"sect").Append(level).Append("\" title=\"");
        sb.Append(EscapeXmlAttr(titleText)).Append("\">\n");

        // Asciidoctor uses <h{level}> inside <section class="sect{level}"> —
        // h1 for sect1, h2 for sect2, etc.
        var htag = level switch { 1 => "h1", 2 => "h2", 3 => "h3", 4 => "h4", 5 => "h5", _ => "h6" };
        sb.Append('<').Append(htag);
        if (section.Id is not null) sb.Append(" id=\"").Append(EscapeXmlAttr(section.Id)).Append('"');
        sb.Append('>');
        if (numberPrefix.Length > 0) EscapeXmlTo(sb, numberPrefix);
        RenderInlines(sb, titleInlines);
        sb.Append("</").Append(htag).Append(">\n");

        // Recurse children.
        var blocks = section.Children.OfType<BlockNode>().ToList();
        for (int i = 0; i < blocks.Count; i++)
            RenderBlock(sb, blocks[i], isLastInContainer: i == blocks.Count - 1);

        sb.Append("</section>\n");
    }

    private static IReadOnlyList<InlineNode> GetTitleInlines(SectionNode s) =>
        s.TitleInlines is { Count: > 0 } ? s.TitleInlines : InlineParser.Parse(s.Title, SubstitutionKind.Normal, new Dictionary<string, string>());

    // ── Paragraph ────────────────────────────────────────────────────────

    private void RenderParagraph(StringBuilder sb, ParagraphNode p, bool isLast)
    {
        sb.Append("<p");
        // Asciidoctor-epub3 adds class="last" only to the absolute final
        // paragraph in the chapter (CSS hook for the chapter-end glyph),
        // not to every last-in-container paragraph.
        var classes = new List<string>();
        if (p.Roles is { Count: > 0 }) classes.AddRange(p.Roles);
        if (ReferenceEquals(p, _lastParagraphInChapter)) classes.Add("last");
        if (classes.Count > 0)
            sb.Append(" class=\"").Append(string.Join(" ", classes.Select(EscapeXmlAttr))).Append('"');
        if (p.Id is not null) sb.Append(" id=\"").Append(EscapeXmlAttr(p.Id)).Append('"');
        sb.Append('>');
        if (p.Inlines.Count > 0) RenderInlines(sb, p.Inlines);
        else EscapeXmlTo(sb, p.Text);
        sb.Append("</p>\n");
    }

    // ── Lists ────────────────────────────────────────────────────────────

    private void RenderList(StringBuilder sb, ListNode list)
    {
        bool isOrdered = list.ListKind == ListKind.Ordered;
        string wrapperClass;
        string innerTag;
        string? olStyle = null;
        string? typeAttr = null;
        if (isOrdered)
        {
            // Asciidoctor-epub3 wraps ordered lists in
            // <div class="ordered-list arabic complex"> with <ol class="arabic">.
            // "complex" is added when items have nested blocks; we always add it
            // for simplicity — the CSS just gives extra spacing in that case.
            olStyle = list.ListStyle ?? "arabic";
            wrapperClass = $"ordered-list {olStyle} complex";
            innerTag = "ol";
            typeAttr = olStyle switch
            {
                "loweralpha" => "a",
                "upperalpha" => "A",
                "lowerroman" => "i",
                "upperroman" => "I",
                _ => null,
            };
        }
        else
        {
            bool isChecklist = list.Children.OfType<ListItemNode>().Any(it => it.Checked is not null);
            wrapperClass = isChecklist ? "itemized-list checklist" : "itemized-list";
            innerTag = "ul";
        }

        sb.Append("<div class=\"").Append(wrapperClass).Append("\">\n");
        sb.Append('<').Append(innerTag);
        if (olStyle is not null) sb.Append(" class=\"").Append(olStyle).Append('"');
        if (typeAttr is not null) sb.Append(" type=\"").Append(typeAttr).Append('"');
        sb.Append(">\n");

        foreach (var child in list.Children)
        {
            if (child is ListItemNode item)
            {
                sb.Append("<li>\n");
                if (item.Checked is not null)
                {
                    sb.Append(item.Checked == true
                        ? "<input checked=\"\" data-item-complete=\"1\" disabled=\"\" type=\"checkbox\"/>"
                        : "<input disabled=\"\" type=\"checkbox\"/>");
                }
                sb.Append("<span class=\"principal\">");
                if (item.Inlines.Count > 0) RenderInlines(sb, item.Inlines);
                else EscapeXmlTo(sb, item.Text);
                sb.Append("</span>\n");
                // Nested blocks under list items render after the span.
                var nested = item.Children.OfType<BlockNode>().ToList();
                for (int i = 0; i < nested.Count; i++)
                    RenderBlock(sb, nested[i], isLastInContainer: false);
                sb.Append("</li>\n");
            }
        }
        sb.Append("</").Append(innerTag).Append(">\n");
        sb.Append("</div>\n");
    }

    private void RenderDescriptionList(StringBuilder sb, DescriptionListNode list)
    {
        // [horizontal] and [qanda] variants reuse different wrappers; default is
        // <div class="description-list"><dl>…
        if (string.Equals(list.Style, "horizontal", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("<div class=\"hdlist\">\n<table>\n");
            foreach (var child in list.Children)
            {
                if (child is DescriptionItemNode item)
                {
                    sb.Append("<tr>\n<td class=\"hdlist1\">");
                    if (item.TermInlines.Count > 0) RenderInlines(sb, item.TermInlines);
                    else if (item.Terms.Count > 0) EscapeXmlTo(sb, item.Terms[0]);
                    sb.Append("</td>\n<td class=\"hdlist2\">\n<p>");
                    if (item.DescriptionInlines.Count > 0) RenderInlines(sb, item.DescriptionInlines);
                    else EscapeXmlTo(sb, item.Description);
                    sb.Append("</p>\n");
                    foreach (var nested in item.Children.OfType<BlockNode>())
                        RenderBlock(sb, nested, isLastInContainer: false);
                    sb.Append("</td>\n</tr>\n");
                }
            }
            sb.Append("</table>\n</div>\n");
            return;
        }

        if (string.Equals(list.Style, "qanda", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("<div class=\"qanda qlist\">\n<ol>\n");
            foreach (var child in list.Children)
            {
                if (child is DescriptionItemNode item)
                {
                    sb.Append("<li>\n<p><em>");
                    if (item.TermInlines.Count > 0) RenderInlines(sb, item.TermInlines);
                    else if (item.Terms.Count > 0) EscapeXmlTo(sb, item.Terms[0]);
                    sb.Append("</em></p>\n<p>");
                    if (item.DescriptionInlines.Count > 0) RenderInlines(sb, item.DescriptionInlines);
                    else EscapeXmlTo(sb, item.Description);
                    sb.Append("</p>\n");
                    foreach (var nested in item.Children.OfType<BlockNode>())
                        RenderBlock(sb, nested, isLastInContainer: false);
                    sb.Append("</li>\n");
                }
            }
            sb.Append("</ol>\n</div>\n");
            return;
        }

        sb.Append("<div class=\"description-list\">\n<dl>\n");
        foreach (var child in list.Children)
        {
            if (child is DescriptionItemNode item)
            {
                sb.Append("<dt>\n<span class=\"term\">");
                if (item.TermInlines.Count > 0) RenderInlines(sb, item.TermInlines);
                else if (item.Terms.Count > 0) EscapeXmlTo(sb, item.Terms[0]);
                sb.Append("</span>\n</dt>\n<dd>\n<span class=\"principal\">");
                if (item.DescriptionInlines.Count > 0) RenderInlines(sb, item.DescriptionInlines);
                else EscapeXmlTo(sb, item.Description);
                sb.Append("</span>\n");
                foreach (var nested in item.Children.OfType<BlockNode>())
                    RenderBlock(sb, nested, isLastInContainer: false);
                sb.Append("</dd>\n");
            }
        }
        sb.Append("</dl>\n</div>\n");
    }

    // ── Delimited blocks ─────────────────────────────────────────────────

    private void RenderDelimitedBlock(StringBuilder sb, DelimitedBlockNode block)
    {
        switch (block.BlockKind)
        {
            case DelimitedBlockKind.Source:
            case DelimitedBlockKind.Listing:
                RenderListingBlock(sb, block);
                break;

            case DelimitedBlockKind.Literal:
                sb.Append("<figure class=\"literal\">\n");
                if (block.Title is not null) { sb.Append("<figcaption>"); RenderTextAsInlines(sb, block.Title); sb.Append("</figcaption>\n"); }
                sb.Append("<pre>");
                EscapeXmlTo(sb, block.Content ?? "");
                sb.Append("</pre>\n</figure>\n");
                break;

            case DelimitedBlockKind.Quote:
                sb.Append("<div class=\"blockquote\">\n<blockquote>\n");
                if (!string.IsNullOrEmpty(block.Content))
                {
                    sb.Append("<p>");
                    RenderTextAsInlines(sb, block.Content!);
                    sb.Append("</p>\n");
                }
                foreach (var b in block.Children.OfType<BlockNode>())
                    RenderBlock(sb, b, isLastInContainer: false);
                if (block.Attribution is not null)
                {
                    sb.Append("<footer>~ ");
                    EscapeXmlTo(sb, block.Attribution);
                    sb.Append("</footer>\n");
                }
                sb.Append("</blockquote>\n</div>\n");
                break;

            case DelimitedBlockKind.Example:
                _exampleCounter++;
                sb.Append("<div class=\"example\"");
                if (block.Id is not null) sb.Append(" id=\"").Append(EscapeXmlAttr(block.Id)).Append('"');
                sb.Append(">\n");
                if (block.Title is not null && !block.IsCollapsible)
                {
                    sb.Append("<div class=\"example-title\">Example ").Append(_exampleCounter).Append(". ");
                    RenderTextAsInlines(sb, block.Title);
                    sb.Append("</div>");
                }
                else if (block.Title is not null)
                {
                    sb.Append("<div class=\"example-title\">");
                    RenderTextAsInlines(sb, block.Title);
                    sb.Append("</div>");
                }
                sb.Append("<div class=\"example-content\">\n");
                foreach (var b in block.Children.OfType<BlockNode>())
                    RenderBlock(sb, b, isLastInContainer: false);
                sb.Append("</div>\n</div>\n");
                break;

            case DelimitedBlockKind.Sidebar:
                sb.Append("<aside class=\"sidebar");
                if (block.Title is not null) sb.Append(" titled");
                sb.Append('"');
                if (block.Title is not null) { sb.Append(" title=\""); EscapeXmlAttrTo(sb, block.Title); sb.Append('"'); }
                sb.Append(" epub:type=\"sidebar\"");
                if (block.Id is not null) sb.Append(" id=\"").Append(EscapeXmlAttr(block.Id)).Append('"');
                sb.Append(">\n");
                if (block.Title is not null) { sb.Append("<h2>"); RenderTextAsInlines(sb, block.Title); sb.Append("</h2>\n"); }
                sb.Append("<div class=\"content\">\n");
                foreach (var b in block.Children.OfType<BlockNode>())
                    RenderBlock(sb, b, isLastInContainer: false);
                sb.Append("</div>\n</aside>\n");
                break;

            case DelimitedBlockKind.Passthrough:
                if (block.Content is not null) { sb.Append(block.Content); sb.Append('\n'); }
                break;

            default:
                // Open block etc.: pass children through.
                foreach (var b in block.Children.OfType<BlockNode>())
                    RenderBlock(sb, b, isLastInContainer: false);
                break;
        }
    }

    private void RenderListingBlock(StringBuilder sb, DelimitedBlockNode block)
    {
        bool hasTitle = block.Title is not null;
        sb.Append("<figure class=\"listing\"");
        if (block.Id is not null) sb.Append(" id=\"").Append(EscapeXmlAttr(block.Id)).Append('"');
        sb.Append('>');
        if (hasTitle)
        {
            _listingCounter++;
            sb.Append("<figcaption>Listing ").Append(_listingCounter).Append(". ");
            RenderTextAsInlines(sb, block.Title!);
            sb.Append("</figcaption>\n");
        }
        else
        {
            sb.Append('\n');
        }
        sb.Append("        <pre class=\"highlight\"><code");
        if (block.Language is not null)
        {
            sb.Append(" class=\"language-").Append(EscapeXmlAttr(block.Language));
            sb.Append("\" data-lang=\"").Append(EscapeXmlAttr(block.Language)).Append('"');
        }
        sb.Append('>');
        EscapeXmlTo(sb, block.Content ?? "");
        sb.Append("</code></pre>\n</figure>\n");
    }

    // ── Admonition ───────────────────────────────────────────────────────

    private void RenderAdmonition(StringBuilder sb, AdmonitionNode adm)
    {
        var typeLower = adm.AdmonitionType.ToLowerInvariant();
        var typeTitle = char.ToUpperInvariant(adm.AdmonitionType[0])
                        + adm.AdmonitionType.Substring(1).ToLowerInvariant();
        // epub:type mapping: note/warning/caution/important all use "notice";
        // tip uses "tip" (asciidoctor-epub3 convention).
        var epubType = typeLower == "tip" ? "tip" : "notice";

        sb.Append("<aside class=\"admonition ").Append(typeLower);
        sb.Append("\" title=\"").Append(typeTitle);
        sb.Append("\" epub:type=\"").Append(epubType).Append("\">\n");
        if (!string.IsNullOrEmpty(adm.Title))
        {
            sb.Append("<h2>");
            RenderTextAsInlines(sb, adm.Title!);
            sb.Append("</h2>\n");
        }
        sb.Append("<div class=\"content\">\n<p>");
        if (adm.Inlines.Count > 0) RenderInlines(sb, adm.Inlines);
        else if (adm.Text is not null) EscapeXmlTo(sb, adm.Text);
        sb.Append("</p>\n");
        foreach (var b in adm.Children.OfType<BlockNode>())
            RenderBlock(sb, b, isLastInContainer: false);
        sb.Append("</div>\n</aside>\n");
    }

    // ── Image / Stem ─────────────────────────────────────────────────────

    private void RenderImage(StringBuilder sb, BlockImageNode img)
    {
        sb.Append("<figure class=\"image\">\n");
        sb.Append("<img src=\"").Append(EscapeXmlAttr(img.Target)).Append('"');
        if (img.Alt.Length > 0) sb.Append(" alt=\"").Append(EscapeXmlAttr(img.Alt)).Append('"');
        if (img.Width is not null) sb.Append(" width=\"").Append(EscapeXmlAttr(img.Width)).Append('"');
        if (img.Height is not null) sb.Append(" height=\"").Append(EscapeXmlAttr(img.Height)).Append('"');
        sb.Append("/>\n");
        if (img.Title is not null)
        {
            _figureCounter++;
            sb.Append("<figcaption>Figure ").Append(_figureCounter).Append(". ");
            RenderTextAsInlines(sb, img.Title);
            sb.Append("</figcaption>\n");
        }
        sb.Append("</figure>\n");
    }

    private void RenderStem(StringBuilder sb, StemBlockNode stem)
    {
        sb.Append("<div class=\"stemblock\">\n<p>");
        if (string.Equals(stem.StemType, "asciimath", StringComparison.OrdinalIgnoreCase))
            sb.Append("\\$").Append(stem.Content).Append("\\$");
        else
            sb.Append("\\[").Append(stem.Content).Append("\\]");
        sb.Append("</p>\n</div>\n");
    }

    // ── Tables ───────────────────────────────────────────────────────────

    private void RenderTable(StringBuilder sb, TableNode table)
    {
        sb.Append("<div class=\"table\">\n");
        if (table.Title is not null)
        {
            _tableCounter++;
            sb.Append("<div class=\"table-title\">Table ").Append(_tableCounter).Append(". ");
            RenderTextAsInlines(sb, table.Title);
            sb.Append("</div>\n");
        }
        sb.Append("<div class=\"content\">\n");
        var frame = table.Frame ?? "all";
        var grid = table.Grid ?? "all";
        sb.Append("<table class=\"table table-framed-").Append(frame);
        sb.Append(" table-grid-").Append(grid).Append("\">\n");

        // <colgroup>
        int colCount = table.Columns?.Count ?? 0;
        if (colCount == 0)
            foreach (var c in table.Children)
                if (c is TableRowNode r0) { colCount = r0.Children.Count; break; }
        if (colCount > 0)
        {
            sb.Append("<colgroup>\n");
            for (int i = 0; i < colCount; i++) sb.Append("<col/>\n");
            sb.Append("</colgroup>\n");
        }

        var rows = table.Children.OfType<TableRowNode>().ToList();
        int bodyStart = 0, bodyEnd = rows.Count;
        if (table.HasHeader && rows.Count > 0)
        {
            sb.Append("<thead>\n");
            AppendRow(sb, rows[0], isHeader: true);
            sb.Append("</thead>\n");
            bodyStart = 1;
        }
        if (table.HasFooter && bodyEnd > bodyStart) bodyEnd--;
        if (bodyEnd > bodyStart)
        {
            sb.Append("<tbody>\n");
            for (int i = bodyStart; i < bodyEnd; i++) AppendRow(sb, rows[i], isHeader: false);
            sb.Append("</tbody>\n");
        }
        if (table.HasFooter && rows.Count > 0)
        {
            sb.Append("<tfoot>\n");
            AppendRow(sb, rows[rows.Count - 1], isHeader: false);
            sb.Append("</tfoot>\n");
        }
        sb.Append("</table>\n</div>\n</div>\n");
    }

    private void AppendRow(StringBuilder sb, TableRowNode row, bool isHeader)
    {
        sb.Append("<tr>\n");
        foreach (var cell in row.Children.OfType<TableCellNode>())
        {
            var halign = cell.Alignment switch
            {
                TableAlignment.Right => "right",
                TableAlignment.Center => "center",
                _ => "left",
            };
            var valign = cell.VerticalAlignment switch
            {
                TableVerticalAlignment.Bottom => "bottom",
                TableVerticalAlignment.Middle => "middle",
                _ => "top",
            };
            sb.Append(isHeader ? "<th class=\"halign-" : "<td class=\"halign-");
            sb.Append(halign).Append(" valign-").Append(valign).Append('"');
            if (cell.ColSpan > 1) sb.Append(" colspan=\"").Append(cell.ColSpan).Append('"');
            if (cell.RowSpan > 1) sb.Append(" rowspan=\"").Append(cell.RowSpan).Append('"');
            sb.Append('>');
            if (isHeader)
            {
                if (cell.Inlines.Count > 0) RenderInlines(sb, cell.Inlines);
                else EscapeXmlTo(sb, cell.Text);
                sb.Append("</th>\n");
            }
            else
            {
                sb.Append("<p class=\"tableblock\">");
                if (cell.Inlines.Count > 0) RenderInlines(sb, cell.Inlines);
                else EscapeXmlTo(sb, cell.Text);
                sb.Append("</p>\n</td>\n");
            }
        }
        sb.Append("</tr>\n");
    }

    // ── Inlines ──────────────────────────────────────────────────────────

    private void RenderInlines(StringBuilder sb, IEnumerable<InlineNode> inlines)
    {
        foreach (var n in inlines) RenderInline(sb, n);
    }

    private void RenderInline(StringBuilder sb, InlineNode node)
    {
        switch (node)
        {
            case TextInlineNode n: EscapeXmlTo(sb, n.Value); break;

            // Asciidoctor-epub3 uses <strong>/<em> (semantic) for the standard
            // *bold* / _italic_ markup. The presentational <b>/<i> are reserved
            // for specific role-class markers (b.button for btn:[], etc.).
            case StrongInlineNode n: sb.Append("<strong>"); RenderInlines(sb, n.Children); sb.Append("</strong>"); break;
            case EmphasisInlineNode n: sb.Append("<em>"); RenderInlines(sb, n.Children); sb.Append("</em>"); break;
            case MonospaceInlineNode n:
                sb.Append("<code class=\"literal\">");
                RenderInlines(sb, n.Children);
                sb.Append("</code>");
                break;
            case HighlightInlineNode n: sb.Append("<mark>"); RenderInlines(sb, n.Children); sb.Append("</mark>"); break;
            case SuperscriptInlineNode n: sb.Append("<sup>"); EscapeXmlTo(sb, n.Content); sb.Append("</sup>"); break;
            case SubscriptInlineNode n: sb.Append("<sub>"); EscapeXmlTo(sb, n.Content); sb.Append("</sub>"); break;
            case PassthroughInlineNode n: sb.Append(n.Content); break;

            case LinkInlineNode n:
                sb.Append("<a href=\"").Append(EscapeXmlAttr(n.Url)).Append("\" class=\"link\">");
                EscapeXmlTo(sb, n.Url);
                sb.Append("</a>");
                break;
            case InlineLinkMacroNode n:
                sb.Append("<a href=\"").Append(EscapeXmlAttr(n.Url)).Append("\" class=\"link\"");
                if (n.Window is not null) sb.Append(" target=\"").Append(EscapeXmlAttr(n.Window)).Append('"');
                sb.Append('>');
                if (n.Label.Length > 0) RenderTextAsInlines(sb, n.Label);
                else EscapeXmlTo(sb, n.Url);
                sb.Append("</a>");
                break;
            case CrossReferenceInlineNode n:
                sb.Append("<a href=\"#").Append(EscapeXmlAttr(n.Target)).Append("\" class=\"xref\">");
                if (n.Label is not null) RenderTextAsInlines(sb, n.Label);
                else { sb.Append('['); EscapeXmlTo(sb, n.Target); sb.Append(']'); }
                sb.Append("</a>");
                break;
            case InterDocumentXrefNode n:
                {
                    var href = n.Path.EndsWith(".adoc", StringComparison.Ordinal)
                        ? n.Path.Substring(0, n.Path.Length - 5) + ".xhtml"
                        : n.Path;
                    if (n.Id is not null) href += "#" + n.Id;
                    sb.Append("<a href=\"").Append(EscapeXmlAttr(href)).Append("\" class=\"xref\">");
                    if (n.Label is not null) RenderTextAsInlines(sb, n.Label);
                    else
                    {
                        var basename = n.Path;
                        var lastSlash = basename.LastIndexOfAny(new[] { '/', '\\' });
                        if (lastSlash >= 0) basename = basename.Substring(lastSlash + 1);
                        var dot = basename.LastIndexOf('.');
                        if (dot > 0) basename = basename.Substring(0, dot);
                        sb.Append('[');
                        EscapeXmlTo(sb, basename);
                        sb.Append(']');
                    }
                    sb.Append("</a>");
                    break;
                }
            case InlineImageNode n:
                sb.Append("<img class=\"inline\" src=\"").Append(EscapeXmlAttr(n.Target)).Append('"');
                if (n.Alt.Length > 0) sb.Append(" alt=\"").Append(EscapeXmlAttr(n.Alt)).Append('"');
                sb.Append("/>");
                break;
            case FootnoteInlineNode n:
                sb.Append("<sup class=\"noteref\">[*]</sup>"); // simplified — full footnote support is post-MVP
                _ = n;
                break;
            case StemInlineNode n:
                if (string.Equals(n.StemType, "asciimath", StringComparison.OrdinalIgnoreCase))
                    sb.Append("\\$").Append(n.Content).Append("\\$");
                else
                    sb.Append("\\(").Append(n.Content).Append("\\)");
                break;
            case InlineMacroNode m:
                RenderInlineMacro(sb, m);
                break;
            default: break;
        }
    }

    private void RenderInlineMacro(StringBuilder sb, InlineMacroNode m)
    {
        switch (m.Name)
        {
            case "kbd":
                var keys = m.Content.Split('+');
                if (keys.Length == 1) { sb.Append("<kbd>"); EscapeXmlTo(sb, keys[0].Trim()); sb.Append("</kbd>"); }
                else
                {
                    sb.Append("<span class=\"keyseq\">");
                    for (int k = 0; k < keys.Length; k++)
                    {
                        if (k > 0) sb.Append('+');
                        sb.Append("<kbd>"); EscapeXmlTo(sb, keys[k].Trim()); sb.Append("</kbd>");
                    }
                    sb.Append("</span>");
                }
                break;
            case "btn":
                sb.Append("<b class=\"button\">"); EscapeXmlTo(sb, m.Content); sb.Append("</b>");
                break;
            case "menu":
                sb.Append("<span class=\"menuseq\"><span class=\"menu\">");
                EscapeXmlTo(sb, m.Target);
                sb.Append("</span>&#160;&#9656; <span class=\"submenu\">");
                EscapeXmlTo(sb, m.Content);
                sb.Append("</span></span>");
                break;
            default:
                EscapeXmlTo(sb, m.Name); sb.Append(':'); EscapeXmlTo(sb, m.Target);
                sb.Append('['); EscapeXmlTo(sb, m.Content); sb.Append(']');
                break;
        }
    }

    private void RenderTextAsInlines(StringBuilder sb, string text)
    {
        var inlines = InlineParser.Parse(text,
            SubstitutionKind.Quotes | SubstitutionKind.Replacements | SubstitutionKind.PostReplacements,
            _document.Attributes);
        RenderInlines(sb, inlines);
    }

    // ── Utilities ────────────────────────────────────────────────────────

    private static string InlinesPlainText(IReadOnlyList<InlineNode> inlines)
    {
        var sb = new StringBuilder();
        foreach (var n in inlines) AppendPlain(sb, n);
        return sb.ToString();
    }

    private static void AppendPlain(StringBuilder sb, InlineNode n)
    {
        switch (n)
        {
            case TextInlineNode t: sb.Append(t.Value); break;
            case StrongInlineNode s: foreach (var c in s.Children) AppendPlain(sb, c); break;
            case EmphasisInlineNode e: foreach (var c in e.Children) AppendPlain(sb, c); break;
            case MonospaceInlineNode m: foreach (var c in m.Children) AppendPlain(sb, c); break;
            case HighlightInlineNode h: foreach (var c in h.Children) AppendPlain(sb, c); break;
            case LinkInlineNode l: sb.Append(l.Url); break;
            case InlineLinkMacroNode l: sb.Append(l.Label.Length > 0 ? l.Label : l.Url); break;
            case CrossReferenceInlineNode x: sb.Append(x.Label ?? x.Target); break;
            case InterDocumentXrefNode x: sb.Append(x.Label ?? x.Path); break;
            case SuperscriptInlineNode s: sb.Append(s.Content); break;
            case SubscriptInlineNode s: sb.Append(s.Content); break;
            case PassthroughInlineNode p: sb.Append(p.Content); break;
            default: break;
        }
    }

    private static void EscapeXmlTo(StringBuilder sb, string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                default: sb.Append(value[i]); break;
            }
        }
    }

    private static string EscapeXmlAttr(string value)
    {
        var sb = new StringBuilder(value.Length);
        EscapeXmlAttrTo(sb, value);
        return sb.ToString();
    }

    private static void EscapeXmlAttrTo(StringBuilder sb, string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                default: sb.Append(value[i]); break;
            }
        }
    }
}
