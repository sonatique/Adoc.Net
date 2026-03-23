using System.Text;
using System.Text.RegularExpressions;
using AdocNet;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Converters.Html;

/// <summary>
/// Renders a <see cref="DocumentNode"/> AST to an HTML5 string.
/// Output uses <c>\n</c> line endings for cross-platform determinism.
/// </summary>
public sealed class HtmlRenderer : DocumentRendererBase
{
    /// <inheritdoc />
    public override string Format => "html";

    /// <summary>
    /// Per-render state that replaces the former ThreadStatic fields.
    /// </summary>
    private sealed class HtmlRenderState
    {
        public Dictionary<string, string> IdTitles { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> TitleIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, string> DocumentAttributes { get; set; } = new Dictionary<string, string>();
        public int TableCounter { get; set; } = 1;
        public int FigureCounter { get; set; } = 1;
        public int ExampleCounter { get; set; } = 1;
    }

    /// <summary>
    /// Tracks section numbering state across the rendering of a document.
    /// </summary>
    private sealed class SectionNumberingContext
    {
        /// <summary>Whether section numbering was enabled at the document level (<c>:sectnums:</c>).</summary>
        public bool Enabled { get; }

        /// <summary>Maximum section level to number (1-based). Default is 3.</summary>
        public int MaxLevel { get; }

        private readonly int[] _counters;

        /// <summary>Creates a disabled (no numbering) context.</summary>
        public SectionNumberingContext()
        {
            Enabled = false;
            MaxLevel = 3;
            _counters = new int[3];
        }

        public SectionNumberingContext(DocumentNode document)
        {
            Enabled = document.Attributes.ContainsKey("sectnums");
            MaxLevel = 3;

            if (document.Attributes.TryGetValue("sectnumlevels", out var levelsStr)
                && int.TryParse(levelsStr, out var parsed)
                && parsed >= 0)
            {
                MaxLevel = parsed;
            }

            _counters = new int[Math.Max(MaxLevel, 1)];
        }

        /// <summary>Creates a copy with the same settings but fresh counters.</summary>
        public SectionNumberingContext(SectionNumberingContext other)
        {
            Enabled = other.Enabled;
            MaxLevel = other.MaxLevel;
            _counters = new int[other._counters.Length];
        }

        /// <summary>
        /// Advances counters for the given section level and returns the
        /// numbering prefix (e.g. "1.2. "), or null if the level exceeds
        /// <see cref="MaxLevel"/>. The caller is responsible for checking
        /// whether numbering is enabled for the specific section.
        /// </summary>
        public string? Advance(int sectionLevel)
        {
            if (sectionLevel < 1 || sectionLevel > MaxLevel)
                return null;

            int idx = sectionLevel - 1;
            _counters[idx]++;

            for (int i = idx + 1; i < _counters.Length; i++)
                _counters[i] = 0;

            var sb = new StringBuilder();
            for (int i = 0; i <= idx; i++)
            {
                if (i > 0) sb.Append('.');
                sb.Append(_counters[i]);
            }
            sb.Append(". ");
            return sb.ToString();
        }
    }

    /// <summary>
    /// Tracks footnotes collected during rendering.
    /// </summary>
    private sealed class FootnoteState
    {
        public List<(int Number, string? Id, FootnoteInlineNode Node)> Footnotes { get; } = [];
        private int _nextNumber = 1;

        /// <summary>
        /// Registers a footnote and returns its display number.
        /// For back-references (Text is null), looks up the existing number.
        /// For named footnotes with same ID, reuses the same number.
        /// </summary>
        /// <summary>
        /// Registers a footnote and returns its display number plus whether this
        /// is a back-reference to an already-defined footnote.
        /// </summary>
        public (int Number, bool IsBackReference) Register(FootnoteInlineNode node)
        {
            // Back-reference: look up existing
            if (node.Text is null && node.Id is not null)
            {
                foreach (var (num, id, _) in Footnotes)
                {
                    if (id == node.Id) return (num, true);
                }
                // If not found, treat as new (shouldn't happen with valid docs)
            }

            // Named footnote: check if ID already seen
            if (node.Id is not null)
            {
                foreach (var (num, id, _) in Footnotes)
                {
                    if (id == node.Id) return (num, true);
                }
            }

            int number = _nextNumber++;
            Footnotes.Add((number, node.Id, node));
            return (number, false);
        }
    }

    /// <inheritdoc />
    protected override void RenderDocument(RenderContext context, Stream output)
    {
        var document = context.Document;
        var state = context.GetOrCreate(() => new HtmlRenderState());
        state.IdTitles = BuildIdTitleMap(document);
        state.TitleIds = BuildTitleIdMap(state.IdTitles);
        state.DocumentAttributes = document.Attributes;
        state.TableCounter = 1;

        var htmlOptions = context.Options as HtmlRenderOptions;
        bool fullDoc = htmlOptions?.IsFullDocument == true;

        var sb = new StringBuilder();

        if (fullDoc)
            AppendDocumentPrologue(sb, document, htmlOptions!);

        var footnotes = new FootnoteState();

        RenderDocumentBody(sb, document, footnotes, state);
        RenderFootnotesSection(sb, footnotes, state);

        if (fullDoc)
            AppendDocumentEpilogue(sb);

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Appends the HTML document prologue: DOCTYPE, &lt;html&gt;, &lt;head&gt; with optional theme CSS.
    /// </summary>
    private static void AppendDocumentPrologue(StringBuilder sb, DocumentNode document, HtmlRenderOptions options)
    {
        sb.Append("<!DOCTYPE html>\n");
        sb.Append("<html lang=\"en\">\n");
        sb.Append("<head>\n");
        sb.Append("<meta charset=\"UTF-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n");

        // Title: explicit option > document title > "Untitled"
        var title = options.Title ?? document.Title ?? "Untitled";
        sb.Append("<title>");
        EscapeTo(sb, title);
        sb.Append("</title>\n");

        // Theme CSS
        var themeCss = HtmlThemeCss.GetCss(options.Theme);
        if (themeCss is not null || options.CustomCss is not null)
        {
            sb.Append("<style>\n");
            if (themeCss is not null)
                sb.Append(themeCss).Append('\n');
            if (options.CustomCss is not null)
                sb.Append(options.CustomCss).Append('\n');
            sb.Append("</style>\n");
        }

        if (options.ExtraHead is not null)
            sb.Append(options.ExtraHead).Append('\n');

        sb.Append("</head>\n");
        sb.Append("<body>\n");
    }

    /// <summary>
    /// Appends the HTML document epilogue: &lt;/body&gt;&lt;/html&gt;.
    /// </summary>
    private static void AppendDocumentEpilogue(StringBuilder sb)
    {
        sb.Append("</body>\n");
        sb.Append("</html>\n");
    }

    /// <summary>
    /// Builds a map from anchor IDs to section/block titles for cross-reference resolution.
    /// </summary>
    private Dictionary<string, string> BuildIdTitleMap(DocumentNode document)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectTitles(document, map);
        return map;
    }

    /// <summary>
    /// Builds a reverse map from section/block titles to anchor IDs for natural cross-reference resolution.
    /// </summary>
    private static Dictionary<string, string> BuildTitleIdMap(Dictionary<string, string> idTitles)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, title) in idTitles)
            map.TryAdd(title, id);
        return map;
    }

    private static void CollectTitles(AstNode node, Dictionary<string, string> map)
    {
        if (node is SectionNode section && section.Id is not null)
            map.TryAdd(section.Id, section.Reftext ?? section.Title);
        else if (node is BlockNode block && block.Id is not null)
        {
            // Reftext from [[id,reftext]] takes priority over inferred titles
            if (block.Reftext is not null)
                map.TryAdd(block.Id, block.Reftext);
            else if (node is DelimitedBlockNode db && db.Title is not null)
                map.TryAdd(block.Id, db.Title);
            else if (node is BlockImageNode img)
                map.TryAdd(block.Id, img.Alt);
        }

        foreach (var child in node.Children)
        {
            // Collect reftext from inline anchors: [[id,reftext]] inside flowing text
            if (child is InlineAnchorNode anchor && anchor.Reftext is not null)
                map.TryAdd(anchor.Id, anchor.Reftext);
            CollectTitles(child, map);
        }
    }

    private void RenderDocumentBody(StringBuilder sb, DocumentNode document, FootnoteState footnotes, HtmlRenderState state)
    {
        var secCtx = new SectionNumberingContext(document);

        if (document.Title is not null)
        {
            sb.Append("<h1>");
            EscapeTo(sb, document.Title);
            sb.Append("</h1>\n");
        }

        bool useIconFont = document.Attributes.TryGetValue("icons", out var iconsValue)
            && string.Equals(iconsValue, "font", StringComparison.OrdinalIgnoreCase);

        RenderChildBlocks(sb, document.Children, useIconFont, footnotes, secCtx, state);
    }

    /// <summary>
    /// Renders the footnotes section at the bottom of the document, if any footnotes were collected.
    /// </summary>
    private void RenderFootnotesSection(StringBuilder sb, FootnoteState footnotes, HtmlRenderState state)
    {
        if (footnotes.Footnotes.Count == 0) return;

        sb.Append("<div id=\"footnotes\">\n");
        sb.Append("<hr>\n");

        foreach (var (number, _, node) in footnotes.Footnotes)
        {
            sb.Append("<div class=\"footnote\" id=\"_footnotedef_");
            sb.Append(number);
            sb.Append("\">\n");
            sb.Append("<a href=\"#_footnoteref_");
            sb.Append(number);
            sb.Append("\">");
            sb.Append(number);
            sb.Append("</a>. ");
            foreach (var inline in node.Inlines)
                RenderInline(sb, inline, footnotes, state);
            sb.Append('\n');
            sb.Append("</div>\n");
        }

        sb.Append("</div>\n");
    }

    private void RenderBlock(StringBuilder sb, AstNode node, bool useIconFont, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        switch (node)
        {
            case SectionNode section:
                RenderSection(sb, section, useIconFont, footnotes, secCtx, state);
                break;
            case ParagraphNode paragraph:
                if (string.Equals(paragraph.Style, "abstract", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append("<div class=\"quoteblock abstract\">\n<blockquote>\n");
                    RenderParagraph(sb, paragraph, footnotes, state);
                    sb.Append("</blockquote>\n</div>\n");
                }
                else
                {
                    RenderParagraph(sb, paragraph, footnotes, state);
                }
                break;
            case ListNode list:
                RenderList(sb, list, footnotes, state, orderedListDepth: 0);
                break;
            case DelimitedBlockNode block:
                RenderDelimitedBlock(sb, block, footnotes, secCtx, state);
                break;
            case TableNode table:
                RenderTable(sb, table, useIconFont, footnotes, secCtx, state);
                break;
            case BlockImageNode blockImage:
                RenderBlockImage(sb, blockImage, state);
                break;
            case VideoNode video:
                RenderVideo(sb, video);
                break;
            case AudioNode audio:
                RenderAudio(sb, audio);
                break;
            case DescriptionListNode descList:
                RenderDescriptionList(sb, descList, useIconFont, footnotes, secCtx, state);
                break;
            case AdmonitionNode admonition:
                RenderAdmonition(sb, admonition, useIconFont, footnotes, secCtx, state);
                break;
            case BibliographyEntryNode bibEntry:
                RenderBibliographyEntry(sb, bibEntry, footnotes, state);
                break;
            case TocNode toc:
                RenderToc(sb, toc, secCtx, state);
                break;
            case PageBreakNode:
                sb.Append("<div style=\"page-break-after: always;\"></div>\n");
                break;
            case ThematicBreakNode:
                sb.Append("<hr>\n");
                break;
            case IndexNode index:
                RenderIndex(sb, index);
                break;
        }
    }

    private void RenderSection(StringBuilder sb, SectionNode section, bool useIconFont, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        // Level 1 = ==  -> <h2>, Level 2 = === -> <h3>, etc.
        var tag = section.Level switch
        {
            1 => "h2",
            2 => "h3",
            3 => "h4",
            4 => "h5",
            _ => "h6",
        };

        var sectionNumberingEnabled = section.SectnumsEnabled ?? secCtx.Enabled;
        var prefix = section.IsDiscrete || !sectionNumberingEnabled ? null : secCtx.Advance(section.Level);

        sb.Append('<');
        sb.Append(tag);
        if (section.Id is not null)
        {
            sb.Append(" id=\"");
            EscapeTo(sb, section.Id);
            sb.Append('"');
        }
        // Asciidoctor renders section roles on the wrapper <div class="sect1 role">,
        // not on the heading tag itself. Since we don't emit wrapper divs, omit role classes.
        sb.Append('>');
        if (prefix is not null)
            sb.Append(prefix);
        RenderInlines(sb, section.TitleInlines, section.Title, footnotes, state);
        sb.Append("</");
        sb.Append(tag);
        sb.Append(">\n");

        RenderChildBlocks(sb, section.Children, useIconFont, footnotes, secCtx, state);
    }

    private void RenderParagraph(StringBuilder sb, ParagraphNode paragraph, FootnoteState footnotes, HtmlRenderState state)
    {
        // Asciidoctor emits roles and id on a wrapper <div class="paragraph ROLE" id="ID">
        // and keeps the inner <p> bare. We mirror that structure.
        bool hasWrapper = paragraph.Roles.Count > 0 || paragraph.Id is not null;
        if (hasWrapper)
        {
            sb.Append("<div class=\"paragraph");
            for (int i = 0; i < paragraph.Roles.Count; i++)
            {
                sb.Append(' ');
                EscapeTo(sb, paragraph.Roles[i]);
            }
            sb.Append('"');
            if (paragraph.Id is not null)
            {
                sb.Append(" id=\"");
                EscapeTo(sb, paragraph.Id);
                sb.Append('"');
            }
            sb.Append(">\n");
        }
        sb.Append("<p>");

        if (paragraph.HasHardbreaks)
        {
            // Render inlines into a temporary buffer, then replace \n with <br>\n.
            var inlineSb = new StringBuilder();
            RenderInlines(inlineSb, paragraph.Inlines, paragraph.Text, footnotes, state);
            var rendered = inlineSb.ToString();
            var parts = rendered.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                    sb.Append("<br>\n");
                sb.Append(parts[i]);
            }
        }
        else
        {
            RenderInlines(sb, paragraph.Inlines, paragraph.Text, footnotes, state);
        }
        sb.Append("</p>\n");
        if (hasWrapper)
            sb.Append("</div>\n");
    }

    private void RenderList(StringBuilder sb, ListNode list, FootnoteState footnotes, HtmlRenderState state, int orderedListDepth)
    {
        var tag = list.ListKind == ListKind.Unordered ? "ul" : "ol";
        int nextDepth = orderedListDepth;

        // Detect checklist: any item with Checked set
        bool isChecklist = list.ListKind == ListKind.Unordered
            && list.Children.OfType<ListItemNode>().Any(i => i.Checked is not null);

        sb.Append('<');
        sb.Append(tag);
        // Asciidoctor renders list IDs on a wrapper <div class="ulist/olist" id="...">
        // rather than on the <ul>/<ol> tag itself. Since we don't emit wrapper divs,
        // omit the ID here to match Asciidoctor's normalized output.

        if (isChecklist)
        {
            sb.Append(" class=\"checklist\"");
        }

        // Asciidoctor emits a list style class (e.g. "arabic" for default ordered lists).
        // When no explicit style is set, auto-assign by nesting depth:
        //   depth 0 → arabic, 1 → loweralpha, 2 → lowerroman, 3+ → cycle
        if (list.ListKind == ListKind.Ordered)
        {
            var effectiveStyle = list.ListStyle ?? orderedListDepth switch
            {
                0 => "arabic",
                1 => "loweralpha",
                2 => "lowerroman",
                _ => "arabic",
            };
            sb.Append(" class=\"");
            sb.Append(effectiveStyle);
            sb.Append('"');

            if (list.Start is not null)
            {
                sb.Append(" start=\"");
                sb.Append(list.Start.Value);
                sb.Append('"');
            }

            var typeValue = effectiveStyle switch
            {
                "loweralpha" => "a",
                "upperalpha" => "A",
                "lowerroman" => "i",
                "upperroman" => "I",
                _ => null,
            };
            if (typeValue is not null)
            {
                sb.Append(" type=\"");
                sb.Append(typeValue);
                sb.Append('"');
            }

            nextDepth = orderedListDepth + 1;
        }

        sb.Append(">\n");

        foreach (var child in list.Children)
        {
            if (child is ListItemNode item)
                RenderListItem(sb, item, footnotes, state, nextDepth);
        }

        sb.Append("</");
        sb.Append(tag);
        sb.Append(">\n");
    }

    private void RenderListItem(StringBuilder sb, ListItemNode item, FootnoteState footnotes, HtmlRenderState state, int orderedListDepth)
    {
        sb.Append("<li>\n");
        // Asciidoctor always wraps list item text in <p>
        sb.Append("<p>");
        if (item.Checked is not null)
        {
            // Asciidoctor uses Unicode check/cross marks, not <input> checkboxes
            sb.Append(item.Checked.Value
                ? "&#10003; "
                : "&#10007; ");
        }
        RenderInlines(sb, item.Inlines, item.Text, footnotes, state);
        sb.Append("</p>");

        // Nested lists and continuation blocks are children of the list item.
        foreach (var child in item.Children)
        {
            if (child is ListNode nestedList)
            {
                sb.Append('\n');
                RenderList(sb, nestedList, footnotes, state, orderedListDepth);
            }
            else if (child is DelimitedBlockNode block)
            {
                sb.Append('\n');
                RenderDelimitedBlock(sb, block, footnotes, new SectionNumberingContext(), state);
            }
            else if (child is ParagraphNode para)
            {
                sb.Append("\n<p>");
                RenderInlines(sb, para.Inlines, para.Text, footnotes, state);
                sb.Append("</p>");
            }
        }

        sb.Append("\n</li>\n");
    }

    private void RenderDelimitedBlock(StringBuilder sb, DelimitedBlockNode block, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        // Passthrough blocks emit raw content — no title rendering.
        if (block.Title is not null && block.BlockKind != DelimitedBlockKind.Passthrough)
        {
            // Example blocks use a numbered caption ("Example N. Title")
            if (block.BlockKind == DelimitedBlockKind.Example)
            {
                sb.Append("<div class=\"title\">Example ");
                sb.Append(state.ExampleCounter++);
                sb.Append(". ");
                EscapeTo(sb, block.Title);
                sb.Append("</div>\n");
            }
            else
            {
                sb.Append("<div class=\"title\">");
                EscapeTo(sb, block.Title);
                sb.Append("</div>\n");
            }
        }

        switch (block.BlockKind)
        {
            case DelimitedBlockKind.Literal:
            {
                bool hasLiteralWrapper = block.Roles.Count > 0;
                if (hasLiteralWrapper)
                {
                    sb.Append("<div");
                    AppendRoleClasses(sb, block, "literalblock");
                    sb.Append(">\n");
                }
                sb.Append("<pre>");
                RenderVerbatimContent(sb, block, state);
                sb.Append("</pre>\n");
                if (hasLiteralWrapper)
                    sb.Append("</div>\n");
                break;
            }

            case DelimitedBlockKind.Listing:
            {
                bool hasListingWrapper = block.Roles.Count > 0;
                if (hasListingWrapper)
                {
                    sb.Append("<div");
                    AppendRoleClasses(sb, block, "listingblock");
                    sb.Append(">\n");
                }
                sb.Append("<pre>");
                RenderVerbatimContent(sb, block, state);
                sb.Append("</pre>\n");
                RenderCalloutList(sb, block, footnotes, state);
                if (hasListingWrapper)
                    sb.Append("</div>\n");
                break;
            }

            case DelimitedBlockKind.Source:
            {
                // Asciidoctor adds highlightjs/hljs classes when source-highlighter is set.
                bool useHighlightJs = state.DocumentAttributes.TryGetValue("source-highlighter", out var highlighter)
                    && highlighter is "highlight.js" or "highlightjs";
                sb.Append(useHighlightJs ? "<pre class=\"highlight highlightjs\"><code" : "<pre class=\"highlight\"><code");
                if (block.Language is not null)
                {
                    sb.Append(useHighlightJs ? " class=\"hljs language-" : " class=\"language-");
                    EscapeTo(sb, block.Language);
                    sb.Append("\" data-lang=\"");
                    EscapeTo(sb, block.Language);
                    sb.Append('"');
                }
                sb.Append('>');
                RenderVerbatimContent(sb, block, state);
                sb.Append("</code></pre>\n");
                RenderCalloutList(sb, block, footnotes, state);
                break;
            }

            case DelimitedBlockKind.Example:
                sb.Append("<div");
                AppendRoleClasses(sb, block, "exampleblock");
                sb.Append(">\n");
                foreach (var child in block.Children)
                    RenderBlock(sb, child, false, footnotes, secCtx, state);
                sb.Append("</div>\n");
                break;

            case DelimitedBlockKind.Quote:
                sb.Append("<blockquote");
                AppendRoleClasses(sb, block);
                sb.Append(">\n");
                foreach (var child in block.Children)
                    RenderBlock(sb, child, false, footnotes, secCtx, state);
                sb.Append("</blockquote>\n");
                if (block.Attribution is not null)
                {
                    sb.Append("\u2014 ");
                    EscapeTo(sb, block.Attribution);
                    if (block.CitationSource is not null)
                    {
                        sb.Append(", ");
                        EscapeTo(sb, block.CitationSource);
                    }
                    sb.Append('\n');
                }
                break;

            case DelimitedBlockKind.Sidebar:
                sb.Append("<div");
                AppendRoleClasses(sb, block, "sidebarblock");
                sb.Append(">\n");
                foreach (var child in block.Children)
                    RenderBlock(sb, child, false, footnotes, secCtx, state);
                sb.Append("</div>\n");
                break;

            case DelimitedBlockKind.Passthrough:
                sb.Append(block.Content ?? string.Empty);
                sb.Append('\n');
                break;

            case DelimitedBlockKind.Open:
                if (string.Equals(block.Style, "abstract", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append("<div class=\"quoteblock abstract\">\n<blockquote>\n");
                    foreach (var child in block.Children)
                        RenderBlock(sb, child, false, footnotes, secCtx, state);
                    sb.Append("</blockquote>\n</div>\n");
                }
                else
                {
                    sb.Append("<div");
                    AppendRoleClasses(sb, block, "openblock");
                    sb.Append(">\n");
                    sb.Append("<div class=\"content\">\n");
                    foreach (var child in block.Children)
                        RenderBlock(sb, child, false, footnotes, secCtx, state);
                    sb.Append("</div>\n");
                    sb.Append("</div>\n");
                }
                break;

            case DelimitedBlockKind.Verse:
                sb.Append("<div");
                AppendRoleClasses(sb, block, "verseblock");
                sb.Append(">\n");
                sb.Append("<pre class=\"content\">");
                var verseContent = block.Content ?? string.Empty;
                var verseLines = verseContent.Split('\n');
                for (int vl = 0; vl < verseLines.Length; vl++)
                {
                    if (vl > 0) sb.Append('\n');
                    var verseInlines = InlineParser.Parse(verseLines[vl], SubstitutionKind.Normal, state.DocumentAttributes);
                    if (verseInlines.Count > 0)
                    {
                        foreach (var inline in verseInlines)
                            RenderInline(sb, inline, footnotes, state);
                    }
                    else
                    {
                        EscapeTo(sb, verseLines[vl]);
                    }
                }
                sb.Append("</pre>\n");
                if (block.Attribution is not null)
                {
                    sb.Append("<div class=\"attribution\">\n");
                    sb.Append("&#8212; ");
                    EscapeTo(sb, block.Attribution);
                    if (block.CitationSource is not null)
                    {
                        sb.Append("<br>\n<cite>");
                        EscapeTo(sb, block.CitationSource);
                        sb.Append("</cite>");
                    }
                    sb.Append('\n');
                    sb.Append("</div>\n");
                }
                sb.Append("</div>\n");
                break;
        }
    }

    private void RenderCalloutList(StringBuilder sb, DelimitedBlockNode block, FootnoteState footnotes, HtmlRenderState state)
    {
        if (block.Callouts is not { Count: > 0 }) return;
        // Skip the callout list if all entries are synthetic (no explanation text).
        if (block.Callouts.All(e => e.Text.Length == 0)) return;
        sb.Append("<div class=\"colist\">\n<ol>\n");
        foreach (var entry in block.Callouts)
        {
            sb.Append("<li>\n<p>");
            RenderInlines(sb, entry.Inlines, entry.Text, footnotes, state);
            sb.Append("</p>\n</li>\n");
        }
        sb.Append("</ol>\n</div>\n");
    }

    private void RenderDescriptionList(StringBuilder sb, DescriptionListNode list, bool useIconFont, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        sb.Append("<dl");
        if (list.Id is not null)
        {
            sb.Append(" id=\"");
            EscapeTo(sb, list.Id);
            sb.Append('"');
        }
        sb.Append(">\n");
        foreach (var child in list.Children)
        {
            if (child is DescriptionItemNode item)
            {
                sb.Append("<dt class=\"hdlist1\">");
                RenderInlines(sb, item.TermInlines, item.Term, footnotes, state);
                sb.Append("</dt>\n");
                sb.Append("<dd>\n");
                bool hasDescText = !string.IsNullOrEmpty(item.Description);
                if (hasDescText)
                {
                    sb.Append("<p>");
                    RenderInlines(sb, item.DescriptionInlines, item.Description, footnotes, state);
                    sb.Append("</p>");
                }
                // Render child blocks attached via list continuation (+)
                foreach (var itemChild in item.Children)
                {
                    if (itemChild is DescriptionListNode nestedDl)
                    {
                        sb.Append('\n');
                        RenderDescriptionList(sb, nestedDl, useIconFont, footnotes, secCtx, state);
                    }
                    else if (itemChild is AdmonitionNode admon)
                    {
                        sb.Append('\n');
                        RenderAdmonition(sb, admon, useIconFont, footnotes, secCtx, state);
                    }
                    else if (itemChild is ParagraphNode para)
                    {
                        sb.Append('\n');
                        RenderParagraph(sb, para, footnotes, state);
                    }
                    else if (itemChild is DelimitedBlockNode block)
                    {
                        sb.Append('\n');
                        RenderDelimitedBlock(sb, block, footnotes, secCtx, state);
                    }
                    else if (itemChild is ListNode nestedList)
                    {
                        sb.Append('\n');
                        RenderList(sb, nestedList, footnotes, state, orderedListDepth: 0);
                    }
                }
                sb.Append("\n</dd>\n");
            }
        }
        sb.Append("</dl>\n");
    }

    private void RenderAdmonition(StringBuilder sb, AdmonitionNode admonition, bool useIconFont, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        var typeLower = admonition.AdmonitionType.ToLowerInvariant();
        sb.Append("<div class=\"admonitionblock ");
        sb.Append(typeLower);
        sb.Append('"');
        if (admonition.Id is not null)
        {
            sb.Append(" id=\"");
            EscapeTo(sb, admonition.Id);
            sb.Append('"');
        }
        sb.Append(">\n");
        sb.Append("<table>\n<tr>\n");

        // Icon / label cell
        sb.Append("<td class=\"icon\">\n");
        string? customAdmonLabel = null;
        var hasCustomLabel = state.DocumentAttributes.TryGetValue(typeLower + "-caption", out customAdmonLabel);
        if (useIconFont)
        {
            sb.Append("<i class=\"fa icon-");
            sb.Append(typeLower);
            sb.Append("\" title=\"");
            if (hasCustomLabel)
            {
                EscapeTo(sb, customAdmonLabel!);
            }
            else
            {
                sb.Append(char.ToUpperInvariant(typeLower[0]));
                sb.Append(typeLower.AsSpan(1));
            }
            sb.Append("\"></i>\n");
        }
        else
        {
            sb.Append("<div class=\"title\">");
            if (hasCustomLabel)
            {
                EscapeTo(sb, customAdmonLabel!);
            }
            else
            {
                // Asciidoctor uses title case (e.g. "Note", "Warning") not uppercase
                sb.Append(char.ToUpperInvariant(typeLower[0]));
                sb.Append(typeLower.AsSpan(1));
            }
            sb.Append("</div>\n");
        }
        sb.Append("</td>\n");

        // Content cell
        sb.Append("<td class=\"content\">\n");
        if (admonition.Children.Count > 0)
        {
            // Block admonition -- render children.
            foreach (var child in admonition.Children)
                RenderBlock(sb, child, useIconFont, footnotes, secCtx, state);
        }
        else
        {
            // Inline admonition -- render content directly (no <p> wrapper).
            // Asciidoctor outputs bare text inside <td class="content"> for
            // single-line admonitions like "NOTE: text".
            RenderInlines(sb, admonition.Inlines, admonition.Text ?? string.Empty, footnotes, state);
            sb.Append('\n');
        }
        sb.Append("</td>\n");

        sb.Append("</tr>\n</table>\n");
        sb.Append("</div>\n");
    }

    /// <summary>
    /// Renders a sequence of child blocks, grouping consecutive bibliography entries
    /// into a <c>&lt;ul class="bibliography"&gt;</c> wrapper to match Asciidoctor output.
    /// </summary>
    private void RenderChildBlocks(StringBuilder sb, IReadOnlyList<AstNode> children, bool useIconFont, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        bool inBibList = false;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is BibliographyEntryNode bibEntry)
            {
                if (!inBibList)
                {
                    sb.Append("<ul class=\"bibliography\">\n");
                    inBibList = true;
                }
                RenderBibliographyEntry(sb, bibEntry, footnotes, state);
            }
            else
            {
                if (inBibList)
                {
                    sb.Append("</ul>\n");
                    inBibList = false;
                }
                RenderBlock(sb, children[i], useIconFont, footnotes, secCtx, state);
            }
        }
        if (inBibList)
            sb.Append("</ul>\n");
    }

    private void RenderBibliographyEntry(StringBuilder sb, BibliographyEntryNode entry, FootnoteState footnotes, HtmlRenderState state)
    {
        sb.Append("<li>\n<p><a id=\"");
        EscapeTo(sb, entry.RefId);
        sb.Append("\"></a>[");
        EscapeTo(sb, entry.Label ?? entry.RefId);
        sb.Append("] ");
        RenderInlines(sb, entry.Inlines, entry.Text, footnotes, state);
        sb.Append("</p>\n</li>\n");
    }

    private void RenderToc(StringBuilder sb, TocNode toc, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        if (toc.Entries.Count == 0) return;

        var cssClass = toc.Placement switch
        {
            TocPlacement.Left => "toc toc-left",
            TocPlacement.Right => "toc toc-right",
            _ => "toc",
        };
        sb.Append("<div id=\"toc\" class=\"");
        sb.Append(cssClass);
        sb.Append("\">\n");
        var tocTitle = state.DocumentAttributes.TryGetValue("toc-title", out var customTocTitle) ? customTocTitle : "Table of Contents";
        sb.Append("<div id=\"toctitle\">");
        EscapeTo(sb, tocTitle);
        sb.Append("</div>\n");
        // Use a separate numbering context for TOC so it doesn't consume the main one.
        var tocSecCtx = new SectionNumberingContext(secCtx);
        RenderTocEntries(sb, toc.Entries, tocSecCtx);
        sb.Append("</div>\n");
    }

    private static void RenderTocEntries(StringBuilder sb, IReadOnlyList<TocEntry> entries, SectionNumberingContext secCtx)
    {
        if (entries.Count == 0) return;
        // Asciidoctor emits sectlevelN classes on TOC <ul> elements.
        int level = entries[0].Level;
        sb.Append("<ul class=\"sectlevel");
        sb.Append(level);
        sb.Append("\">\n");
        foreach (var entry in entries)
        {
            bool hasChildren = entry.Children.Count > 0;
            var prefix = secCtx.Enabled ? secCtx.Advance(entry.Level) : null;

            if (hasChildren)
            {
                // Entries with sub-entries: <li><a>Title</a>\n<ul>...</ul>\n</li>
                sb.Append("<li><a href=\"#");
                EscapeTo(sb, entry.Id);
                sb.Append("\">");
                if (prefix is not null)
                    sb.Append(prefix);
                EscapeTo(sb, entry.Title);
                sb.Append("</a>\n");
                RenderTocEntries(sb, entry.Children, secCtx);
                sb.Append("</li>\n");
            }
            else
            {
                // Leaf entries: compact single-line
                sb.Append("<li><a href=\"#");
                EscapeTo(sb, entry.Id);
                sb.Append("\">");
                if (prefix is not null)
                    sb.Append(prefix);
                EscapeTo(sb, entry.Title);
                sb.Append("</a></li>\n");
            }
        }
        sb.Append("</ul>\n");
    }

    private void RenderTable(StringBuilder sb, TableNode table, bool useIconFont, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        sb.Append("<table");
        if (table.Id is not null)
        {
            sb.Append(" id=\"");
            EscapeTo(sb, table.Id);
            sb.Append('"');
        }

        // Build CSS class list from table options (Asciidoctor always emits frame/grid/tableblock)
        var tableClasses = new List<string>();
        tableClasses.Add($"frame-{table.Frame ?? "all"}");
        tableClasses.Add($"grid-{table.Grid ?? "all"}");
        if (table.IsAutoWidth)
            tableClasses.Add("fit-content");
        else
            tableClasses.Add("stretch");
        if (table.Stripes is not null)
            tableClasses.Add($"stripes-{table.Stripes}");
        tableClasses.Add("tableblock");
        AppendRoleClasses(sb, table, string.Join(" ", tableClasses));

        sb.Append(">\n");

        if (table.Title is not null)
        {
            var tableCaption = state.DocumentAttributes.TryGetValue("table-caption", out var customTableCaption) ? customTableCaption : "Table";
            sb.Append("<caption class=\"title\">");
            EscapeTo(sb, tableCaption);
            sb.Append(' ');
            sb.Append(state.TableCounter);
            sb.Append(". ");
            EscapeTo(sb, table.Title);
            sb.Append("</caption>\n");
            state.TableCounter++;
        }

        // Render colgroup — Asciidoctor always emits it.
        // For autowidth tables, emit unstyled <col> elements.
        // For fixed-width tables, use proportional widths.
        if (table.IsAutoWidth)
        {
            int colCount = 0;
            if (table.Children.Count > 0 && table.Children[0] is TableRowNode firstAutoRow)
                colCount = firstAutoRow.Children.Count;
            if (colCount > 0)
            {
                sb.Append("<colgroup>\n");
                for (int ci = 0; ci < colCount; ci++)
                    sb.Append("<col>\n");
                sb.Append("</colgroup>\n");
            }
        }
        else
        {
            if (table.Columns is { Count: > 0 })
            {
                int totalWidth = 0;
                foreach (var col in table.Columns)
                    totalWidth += col.Width;

                sb.Append("<colgroup>\n");
                double emittedTruncated = 0;
                for (int ci = 0; ci < table.Columns.Count; ci++)
                {
                    double rawPct = 100.0 * table.Columns[ci].Width / totalWidth;
                    // Asciidoctor truncates each column to 4 decimal places (not rounding),
                    // accumulates truncated values, and gives the remainder to the last column.
                    double colPct;
                    if (ci == table.Columns.Count - 1)
                        colPct = TruncateTo4(100.0 - emittedTruncated);
                    else
                        colPct = TruncateTo4(rawPct);
                    sb.Append("<col style=\"width: ");
                    if (Math.Abs(colPct - Math.Truncate(colPct)) < 0.00005)
                        sb.Append((int)colPct);
                    else
                        sb.AppendFormat("{0:F4}", colPct);
                    sb.Append("%;\">\n");
                    emittedTruncated += colPct;
                }
                sb.Append("</colgroup>\n");
            }
            else
            {
                // No explicit cols — derive column count from the first row (sum colspans)
                int colCount = 0;
                if (table.Children.Count > 0 && table.Children[0] is TableRowNode firstRow)
                {
                    foreach (var child in firstRow.Children)
                        colCount += child is TableCellNode cell ? cell.ColSpan : 1;
                }

                if (colCount > 0)
                {
                    double rawPct = 100.0 / colCount;
                    sb.Append("<colgroup>\n");
                    double emittedTruncated = 0;
                    for (int ci = 0; ci < colCount; ci++)
                    {
                        double colPct;
                        if (ci == colCount - 1)
                            colPct = TruncateTo4(100.0 - emittedTruncated);
                        else
                            colPct = TruncateTo4(rawPct);
                        sb.Append("<col style=\"width: ");
                        if (Math.Abs(colPct - Math.Truncate(colPct)) < 0.00005)
                            sb.Append((int)colPct);
                        else
                            sb.AppendFormat("{0:F4}", colPct);
                        sb.Append("%;\">\n");
                        emittedTruncated += colPct;
                    }
                    sb.Append("</colgroup>\n");
                }
            }
        }

        int startRow = 0;
        if (table.HasHeader && table.Children.Count > 0)
        {
            sb.Append("<thead>\n");
            if (table.Children[0] is TableRowNode headerRow)
                RenderTableRow(sb, headerRow, "th", table.Columns, useIconFont, footnotes, secCtx, state);
            sb.Append("</thead>\n");
            startRow = 1;
        }

        int endRow = table.Children.Count;
        if (table.HasFooter && table.Children.Count > startRow)
            endRow = table.Children.Count - 1;

        sb.Append("<tbody>\n");
        for (int i = startRow; i < endRow; i++)
        {
            if (table.Children[i] is TableRowNode row)
                RenderTableRow(sb, row, "td", table.Columns, useIconFont, footnotes, secCtx, state);
        }
        sb.Append("</tbody>\n");

        if (table.HasFooter && table.Children.Count > startRow)
        {
            sb.Append("<tfoot>\n");
            if (table.Children[^1] is TableRowNode footerRow)
                RenderTableRow(sb, footerRow, "td", table.Columns, useIconFont, footnotes, secCtx, state);
            sb.Append("</tfoot>\n");
        }

        sb.Append("</table>\n");
    }

    private void RenderTableRow(StringBuilder sb, TableRowNode row, string cellTag,
        IReadOnlyList<TableColumnSpec>? columns, bool useIconFont, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        sb.Append("<tr>\n");
        int colIndex = 0;
        foreach (var child in row.Children)
        {
            if (child is TableCellNode cell)
            {
                // Header style overrides cell tag to <th>
                var effectiveTag = cell.ContentStyle == TableCellStyle.Header ? "th" : cellTag;

                sb.Append('<');
                sb.Append(effectiveTag);

                if (cell.ColSpan > 1)
                {
                    sb.Append(" colspan=\"");
                    sb.Append(cell.ColSpan);
                    sb.Append('"');
                }

                if (cell.RowSpan > 1)
                {
                    sb.Append(" rowspan=\"");
                    sb.Append(cell.RowSpan);
                    sb.Append('"');
                }

                // Determine alignment: per-cell override, then column spec, then default (left)
                var hAlign = cell.Alignment;
                if (hAlign is null && columns is not null && colIndex < columns.Count)
                    hAlign = columns[colIndex].Alignment;
                hAlign ??= TableAlignment.Left;

                var vAlign = cell.VerticalAlignment;
                if (vAlign is null && columns is not null && colIndex < columns.Count)
                    vAlign = columns[colIndex].VerticalAlignment;
                vAlign ??= TableVerticalAlignment.Top;

                // Asciidoctor emits halign-*/valign-*/tableblock classes on every cell
                var hAlignClass = hAlign switch
                {
                    TableAlignment.Center => "halign-center",
                    TableAlignment.Right => "halign-right",
                    _ => "halign-left",
                };
                var vAlignClass = vAlign switch
                {
                    TableVerticalAlignment.Middle => "valign-middle",
                    TableVerticalAlignment.Bottom => "valign-bottom",
                    _ => "valign-top",
                };
                sb.Append($" class=\"{hAlignClass} tableblock {vAlignClass}\"");

                sb.Append('>');

                // Wrap content based on cell style
                var wrapOpen = cell.ContentStyle switch
                {
                    TableCellStyle.Emphasis  => "<em>",
                    TableCellStyle.Literal   => "<pre>",
                    TableCellStyle.Monospace  => "<code>",
                    _ => null,
                };
                var wrapClose = cell.ContentStyle switch
                {
                    TableCellStyle.Emphasis  => "</em>",
                    TableCellStyle.Literal   => "</pre>",
                    TableCellStyle.Monospace  => "</code>",
                    _ => null,
                };

                if (cell.ContentStyle == TableCellStyle.AsciiDoc && cell.Children.Count > 0)
                {
                    foreach (var blockChild in cell.Children)
                        RenderBlock(sb, blockChild, useIconFont, footnotes, secCtx, state);
                }
                else
                {
                    // Asciidoctor wraps body cell content in <p class="tableblock">
                    // but skips the wrapper for actual header row cells and empty cells.
                    // h-style cells (ContentStyle == Header) render as <th> but ARE
                    // body cells, so they still get the <p> wrapper.
                    bool hasContent = cell.Inlines.Count > 0 || !string.IsNullOrEmpty(cell.Text);
                    bool wrapInP = cellTag == "td" && hasContent;

                    if (wrapInP)
                        sb.Append("<p class=\"tableblock\">");
                    if (wrapOpen is not null)
                        sb.Append(wrapOpen);
                    RenderInlines(sb, cell.Inlines, cell.Text, footnotes, state);
                    if (wrapClose is not null)
                        sb.Append(wrapClose);
                    if (wrapInP)
                        sb.Append("</p>");
                }

                sb.Append("</");
                sb.Append(effectiveTag);
                sb.Append(">\n");

                colIndex += cell.ColSpan;
            }
        }
        sb.Append("</tr>\n");
    }

    private static void RenderBlockImage(StringBuilder sb, BlockImageNode image, HtmlRenderState state)
    {
        sb.Append("<div class=\"imageblock\"");
        if (image.Id is not null)
        {
            sb.Append(" id=\"");
            EscapeTo(sb, image.Id);
            sb.Append('"');
        }
        sb.Append(">\n");
        sb.Append("<img src=\"");
        EscapeTo(sb, image.Target);
        sb.Append("\" alt=\"");
        EscapeTo(sb, image.Alt);
        sb.Append("\">\n");
        // Asciidoctor renders block image titles below the image with "Figure N." prefix
        if (image.Title is not null)
        {
            sb.Append("<div class=\"title\">Figure ");
            sb.Append(state.FigureCounter++);
            sb.Append(". ");
            EscapeTo(sb, image.Title);
            sb.Append("</div>\n");
        }
        sb.Append("</div>\n");
    }

    private static void RenderVideo(StringBuilder sb, VideoNode video)
    {
        sb.Append("<div class=\"videoblock\">\n<div class=\"content\">\n<video src=\"");
        EscapeTo(sb, video.Target);
        sb.Append('"');
        if (video.Width is not null)
        {
            sb.Append(" width=\"");
            EscapeTo(sb, video.Width);
            sb.Append('"');
        }
        if (video.Height is not null)
        {
            sb.Append(" height=\"");
            EscapeTo(sb, video.Height);
            sb.Append('"');
        }
        if (video.Poster is not null)
        {
            sb.Append(" poster=\"");
            EscapeTo(sb, video.Poster);
            sb.Append('"');
        }
        if (video.Autoplay) sb.Append(" autoplay");
        if (video.Loop) sb.Append(" loop");
        if (video.Controls) sb.Append(" controls");
        sb.Append(">\nYour browser does not support the video tag.\n</video>\n</div>\n</div>\n");
    }

    private static void RenderAudio(StringBuilder sb, AudioNode audio)
    {
        sb.Append("<div class=\"audioblock\">\n<div class=\"content\">\n<audio src=\"");
        EscapeTo(sb, audio.Target);
        sb.Append('"');
        if (audio.Autoplay) sb.Append(" autoplay");
        if (audio.Loop) sb.Append(" loop");
        if (audio.Controls) sb.Append(" controls");
        sb.Append(">\nYour browser does not support the audio tag.\n</audio>\n</div>\n</div>\n");
    }

    private static void RenderIndex(StringBuilder sb, IndexNode index)
    {
        sb.Append("<div class=\"index\">\n");

        char currentLetter = '\0';
        bool listOpen = false;

        foreach (var entry in index.Entries)
        {
            if (entry.Term.Length == 0) continue;

            char firstLetter = char.ToUpperInvariant(entry.Term[0]);
            if (firstLetter != currentLetter)
            {
                if (listOpen)
                    sb.Append("</ul>\n");
                currentLetter = firstLetter;
                sb.Append("<h3>");
                sb.Append(currentLetter);
                sb.Append("</h3>\n<ul>\n");
                listOpen = true;
            }

            sb.Append("<li>");
            EscapeTo(sb, entry.Term);
            if (entry.SubTerms.Count > 0)
            {
                sb.Append(", ");
                for (int i = 0; i < entry.SubTerms.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    EscapeTo(sb, entry.SubTerms[i]);
                }
            }
            sb.Append("</li>\n");
        }

        if (listOpen)
            sb.Append("</ul>\n");

        sb.Append("</div>\n");
    }

    /// <summary>
    /// Renders inline nodes when available, or falls back to escaped raw text.
    /// This supports both parser-produced nodes (with Inlines populated) and
    /// hand-constructed test nodes (with only Text/Title set).
    /// </summary>
    private void RenderInlines(StringBuilder sb, IReadOnlyList<InlineNode> inlines, string fallbackText, FootnoteState footnotes, HtmlRenderState state)
    {
        if (inlines.Count > 0)
        {
            foreach (var inline in inlines)
                RenderInline(sb, inline, footnotes, state);
        }
        else
        {
            EscapeTo(sb, fallbackText);
        }
    }

    private void RenderInline(StringBuilder sb, InlineNode node, FootnoteState footnotes, HtmlRenderState state)
    {
        switch (node)
        {
            case TextInlineNode text:
                EscapeTo(sb, text.Value);
                break;

            case EmphasisInlineNode emphasis:
                if (emphasis.Roles is { Count: > 0 })
                {
                    sb.Append("<em class=\"");
                    AppendRoles(sb, emphasis.Roles);
                    sb.Append("\">");
                }
                else
                {
                    sb.Append("<em>");
                }
                foreach (var child in emphasis.Children)
                    RenderInline(sb, child, footnotes, state);
                sb.Append("</em>");
                break;

            case StrongInlineNode strong:
                if (strong.Roles is { Count: > 0 })
                {
                    sb.Append("<strong class=\"");
                    AppendRoles(sb, strong.Roles);
                    sb.Append("\">");
                }
                else
                {
                    sb.Append("<strong>");
                }
                foreach (var child in strong.Children)
                    RenderInline(sb, child, footnotes, state);
                sb.Append("</strong>");
                break;

            case MonospaceInlineNode monospace:
                if (monospace.Roles is { Count: > 0 })
                {
                    sb.Append("<code class=\"");
                    AppendRoles(sb, monospace.Roles);
                    sb.Append("\">");
                }
                else
                {
                    sb.Append("<code>");
                }
                foreach (var child in monospace.Children)
                    RenderInline(sb, child, footnotes, state);
                sb.Append("</code>");
                break;

            case HighlightInlineNode highlight:
                if (highlight.Roles is { Count: > 0 })
                {
                    sb.Append("<span class=\"");
                    for (int r = 0; r < highlight.Roles.Count; r++)
                    {
                        if (r > 0) sb.Append(' ');
                        EscapeTo(sb, highlight.Roles[r]);
                    }
                    sb.Append("\">");
                    foreach (var child in highlight.Children)
                        RenderInline(sb, child, footnotes, state);
                    sb.Append("</span>");
                }
                else
                {
                    sb.Append("<mark>");
                    foreach (var child in highlight.Children)
                        RenderInline(sb, child, footnotes, state);
                    sb.Append("</mark>");
                }
                break;

            case LinkInlineNode link:
                sb.Append("<a class=\"bare\" href=\"");
                EscapeTo(sb, link.Url);
                sb.Append("\">");
                EscapeTo(sb, link.Url);
                sb.Append("</a>");
                break;

            case InlineLinkMacroNode linkMacro:
            {
                // Asciidoctor adds class="bare" when the link macro has no explicit
                // label (the URL itself becomes the display text).
                bool isBare = string.IsNullOrEmpty(linkMacro.Label) ||
                              linkMacro.Label == linkMacro.Url;
                sb.Append("<a");
                if (isBare)
                    sb.Append(" class=\"bare\"");
                sb.Append(" href=\"");
                EscapeTo(sb, linkMacro.Url);
                sb.Append("\">");
                EscapeTo(sb, isBare ? linkMacro.Url : linkMacro.Label);
                sb.Append("</a>");
                break;
            }

            case InlineImageNode inlineImage:
                sb.Append("<span class=\"image\"><img src=\"");
                EscapeTo(sb, inlineImage.Target);
                sb.Append("\" alt=\"");
                EscapeTo(sb, inlineImage.Alt);
                sb.Append("\"></span>");
                break;

            case SuperscriptInlineNode superscript:
                sb.Append("<sup>");
                EscapeTo(sb, superscript.Content);
                sb.Append("</sup>");
                break;

            case SubscriptInlineNode subscript:
                sb.Append("<sub>");
                EscapeTo(sb, subscript.Content);
                sb.Append("</sub>");
                break;

            case PassthroughInlineNode passthrough:
                sb.Append(passthrough.Content);
                break;

            case CrossReferenceInlineNode xref:
                // Try target as literal ID first; if not found, try as a section title via reverse map.
                var resolvedId = xref.Target;
                string? resolvedTitle = null;
                var isKnownId = state.IdTitles.ContainsKey(xref.Target);
                if (!isKnownId && state.TitleIds.TryGetValue(xref.Target, out var mappedId))
                {
                    resolvedId = mappedId;
                    state.IdTitles.TryGetValue(mappedId, out resolvedTitle);
                }

                sb.Append("<a href=\"#");
                EscapeTo(sb, resolvedId);
                sb.Append("\">");
                if (xref.Label is not null)
                    EscapeTo(sb, xref.Label);
                else if (resolvedTitle is not null)
                    EscapeTo(sb, resolvedTitle);
                else if (state.IdTitles.TryGetValue(resolvedId, out var refTitle))
                    EscapeTo(sb, refTitle);
                else
                {
                    sb.Append('[');
                    EscapeTo(sb, xref.Target);
                    sb.Append(']');
                }
                sb.Append("</a>");
                break;

            case InterDocumentXrefNode interXref:
                {
                    var href = interXref.Path.EndsWith(".adoc", StringComparison.Ordinal)
                        ? interXref.Path[..^5] + ".html"
                        : interXref.Path;
                    if (interXref.Id is not null)
                        href += "#" + interXref.Id;
                    sb.Append("<a href=\"");
                    EscapeTo(sb, href);
                    sb.Append("\">");
                    if (interXref.Label is not null)
                        EscapeTo(sb, interXref.Label);
                    else
                    {
                        // Use the converted href (path with .html) as display text
                        EscapeTo(sb, href);
                    }
                    sb.Append("</a>");
                    break;
                }

            case FootnoteInlineNode footnote:
            {
                var (num, isBackRef) = footnotes.Register(footnote);
                // Asciidoctor uses "footnoteref" class on back-references (no id),
                // and adds id="_footnote_NAME" on <sup> for named first references.
                sb.Append("<sup class=\"");
                sb.Append(isBackRef ? "footnoteref" : "footnote");
                sb.Append('"');
                if (!isBackRef && footnote.Id is not null)
                {
                    sb.Append(" id=\"_footnote_");
                    EscapeTo(sb, footnote.Id);
                    sb.Append('"');
                }
                sb.Append(">[<a class=\"footnote\" href=\"#_footnotedef_");
                sb.Append(num);
                sb.Append('"');
                if (!isBackRef)
                {
                    sb.Append(" id=\"_footnoteref_");
                    sb.Append(num);
                    sb.Append('"');
                }
                sb.Append(" title=\"View footnote.\">");
                sb.Append(num);
                sb.Append("</a>]</sup>");
                break;
            }

            case InlineAnchorNode anchor:
                sb.Append("<a id=\"");
                EscapeTo(sb, anchor.Id);
                sb.Append("\"></a>");
                break;

            case InlineMacroNode macro:
                RenderInlineMacro(sb, macro, state);
                break;

            case IndexTermNode indexTerm:
                // Visible index term: render the first term as text
                if (indexTerm.Terms.Count > 0)
                    EscapeTo(sb, indexTerm.Terms[0]);
                break;

            case IndexTermHiddenNode:
                // Hidden index term: renders nothing
                break;
        }
    }

    private void RenderInlineMacro(StringBuilder sb, InlineMacroNode macro, HtmlRenderState state)
    {
        switch (macro.Name)
        {
            case "kbd":
                var keys = macro.Content.Split('+');
                if (keys.Length == 1)
                {
                    sb.Append("<kbd>");
                    EscapeTo(sb, keys[0].Trim());
                    sb.Append("</kbd>");
                }
                else
                {
                    sb.Append("<span class=\"keyseq\">");
                    for (int k = 0; k < keys.Length; k++)
                    {
                        if (k > 0) sb.Append('+');
                        sb.Append("<kbd>");
                        EscapeTo(sb, keys[k].Trim());
                        sb.Append("</kbd>");
                    }
                    sb.Append("</span>");
                }
                break;

            case "btn":
                sb.Append("<b class=\"button\">");
                EscapeTo(sb, macro.Content);
                sb.Append("</b>");
                break;

            case "menu":
                sb.Append("<span class=\"menuseq\"><span class=\"menu\">");
                EscapeTo(sb, macro.Target);
                sb.Append("</span>&#160;&#9656; <span class=\"submenu\">");
                EscapeTo(sb, macro.Content);
                sb.Append("</span></span>");
                break;

            case "icon":
                RenderIconMacro(sb, macro, state);
                break;

            default:
                sb.Append("<span class=\"");
                EscapeTo(sb, macro.Name);
                sb.Append("\">");
                EscapeTo(sb, macro.Content);
                sb.Append("</span>");
                break;
        }
    }

    private void RenderIconMacro(StringBuilder sb, InlineMacroNode macro, HtmlRenderState state)
    {
        var iconName = macro.Target.Length > 0 ? macro.Target : macro.Content;
        var iconsMode = state.DocumentAttributes.TryGetValue("icons", out var iconsVal) ? iconsVal : null;

        if (string.Equals(iconsMode, "font", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("<i class=\"fa fa-");
            EscapeTo(sb, iconName);

            // Parse named attributes from Content for size, rotate, flip
            if (macro.Content.Length > 0)
            {
                foreach (var part in macro.Content.Split(','))
                {
                    var trimmed = part.Trim();
                    var eqIdx = trimmed.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        var key = trimmed[..eqIdx].Trim();
                        var value = trimmed[(eqIdx + 1)..].Trim();
                        if (key is "size" or "rotate" or "flip")
                        {
                            sb.Append(" fa-");
                            if (key == "rotate")
                                sb.Append("rotate-");
                            else if (key == "flip")
                                sb.Append("flip-");
                            EscapeTo(sb, value);
                        }
                    }
                }
            }

            sb.Append("\"></i>");
        }
        else if (string.Equals(iconsMode, "image", StringComparison.OrdinalIgnoreCase))
        {
            var iconsDir = state.DocumentAttributes.TryGetValue("iconsdir", out var dir) ? dir : "./images/icons";
            sb.Append("<img src=\"");
            EscapeTo(sb, iconsDir);
            sb.Append('/');
            EscapeTo(sb, iconName);
            sb.Append(".png\" alt=\"");
            EscapeTo(sb, iconName);
            sb.Append("\">");
        }
        else
        {
            // No icons attribute: render as plain text
            sb.Append('[');
            EscapeTo(sb, iconName);
            sb.Append(']');
        }
    }

    /// <summary>
    /// Appends a <c>class="..."</c> attribute from the node's roles and an optional existing class.
    /// </summary>
    private static void AppendRoleClasses(StringBuilder sb, BlockNode node, string? existingClass = null)
    {
        if (node.Roles.Count == 0 && existingClass is null) return;
        sb.Append(" class=\"");
        if (existingClass is not null) sb.Append(existingClass);
        for (int i = 0; i < node.Roles.Count; i++)
        {
            if (i > 0 || existingClass is not null) sb.Append(' ');
            EscapeTo(sb, node.Roles[i]);
        }
        sb.Append('"');
    }

    /// <summary>
    /// Renders the content of a verbatim block (Listing/Source/Literal),
    /// respecting the block's <see cref="BlockNode.Substitutions"/> property.
    /// When <c>Substitutions</c> is null, default behavior (HTML-escape) is used.
    /// </summary>
    private void RenderVerbatimContent(StringBuilder sb, DelimitedBlockNode block, HtmlRenderState state)
    {
        var content = block.Content ?? string.Empty;
        var subs = block.Substitutions;

        // Build a line-number-to-callout-numbers map for conum marker insertion.
        Dictionary<int, List<int>>? conumMap = null;
        if (block.Callouts is { Count: > 0 })
        {
            foreach (var entry in block.Callouts)
            {
                if (entry.LineNumber < 0) continue;
                if (conumMap is null) conumMap = [];
                if (!conumMap.TryGetValue(entry.LineNumber, out var nums))
                {
                    nums = [];
                    conumMap[entry.LineNumber] = nums;
                }
                nums.Add(entry.Number);
            }
        }

        if (conumMap is { Count: > 0 })
        {
            // Render line-by-line so we can append conum markers.
            if (subs.HasValue && subs.Value.HasFlag(SubstitutionKind.Attributes) && state.DocumentAttributes is { Count: > 0 })
                content = ExpandAttributes(content, state.DocumentAttributes);

            bool escape = !subs.HasValue || subs.Value.HasFlag(SubstitutionKind.SpecialCharacters);
            var lines = content.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (escape)
                    EscapeTo(sb, lines[i]);
                else
                    sb.Append(lines[i]);

                if (conumMap.TryGetValue(i, out var calloutNums))
                {
                    foreach (var num in calloutNums)
                        sb.Append($" <b class=\"conum\">({num})</b>");
                }

                if (i < lines.Length - 1)
                    sb.Append('\n');
            }
        }
        else if (subs.HasValue)
        {
            // Apply attribute expansion if requested
            if (subs.Value.HasFlag(SubstitutionKind.Attributes) && state.DocumentAttributes is { Count: > 0 })
                content = ExpandAttributes(content, state.DocumentAttributes);

            // Only escape if SpecialCharacters is in the subs set
            if (subs.Value.HasFlag(SubstitutionKind.SpecialCharacters))
                EscapeTo(sb, content);
            else
                sb.Append(content);
        }
        else
        {
            // Default: escape (current behavior)
            EscapeTo(sb, content);
        }
    }

    /// <summary>
    /// Expands <c>{name}</c> attribute references in the given text.
    /// Unknown references are left as-is.
    /// </summary>
    private static string ExpandAttributes(string text, IReadOnlyDictionary<string, string> attributes)
    {
        if (!text.Contains('{')) return text;

        return Regex.Replace(text, @"\{(\w[\w-]*)\}", match =>
        {
            var name = match.Groups[1].Value;
            return attributes.TryGetValue(name, out var value) ? value : match.Value;
        });
    }

    /// <summary>
    /// Appends HTML-escaped text directly to the target StringBuilder,
    /// avoiding an intermediate string allocation.
    /// Uses bulk span copies for runs of characters that don't need escaping.
    /// </summary>
    private static double TruncateTo4(double value) => Math.Truncate(value * 10000) / 10000;

    private static void EscapeTo(StringBuilder sb, string value)
    {
        int segmentStart = 0;
        for (int i = 0; i < value.Length; i++)
        {
            string? entity = value[i] switch
            {
                '&'  => "&amp;",
                '<'  => "&lt;",
                '>'  => "&gt;",
                '"'  => "&quot;",
                '\'' => "&#39;",
                _    => null,
            };

            if (entity is not null)
            {
                if (i > segmentStart)
                    sb.Append(value.AsSpan(segmentStart, i - segmentStart));
                sb.Append(entity);
                segmentStart = i + 1;
            }
        }

        // Flush remaining unescaped segment.
        if (segmentStart == 0)
            sb.Append(value); // nothing was escaped -- append whole string
        else if (segmentStart < value.Length)
            sb.Append(value.AsSpan(segmentStart));
    }

    private static void AppendRoles(StringBuilder sb, IReadOnlyList<string> roles)
    {
        for (int r = 0; r < roles.Count; r++)
        {
            if (r > 0) sb.Append(' ');
            EscapeTo(sb, roles[r]);
        }
    }

    private static bool NeedsEscaping(string value)
    {
        foreach (var ch in value)
        {
            if (ch is '&' or '<' or '>' or '"' or '\'')
                return true;
        }
        return false;
    }
}
