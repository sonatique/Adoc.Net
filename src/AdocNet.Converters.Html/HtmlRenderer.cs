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
public sealed partial class HtmlRenderer : DocumentRendererBase
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
        public bool EnableSyntaxHighlighting { get; set; }
        public bool EnableIncrementalMarkers { get; set; }
        public bool DataUriEnabled { get; set; }
        public string? BaseDirectory { get; set; }
        public string? ImagesDir { get; set; }
        public int AppendixCounter { get; set; }
        public int PartCounter { get; set; }
        /// <summary>Maps section IDs to their numbering strings (e.g. "1.2") for xrefstyle.</summary>
        public Dictionary<string, string> IdNumbers { get; set; } = new(StringComparer.Ordinal);
        /// <summary>When true, in-content TOC rendering is suppressed (TOC was already
        /// rendered inside #header for :toc: left/right). Set in RenderDocumentBody.</summary>
        public bool SkipInlineToc { get; set; }
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

    private RenderContext? _currentContext;
    private RenderOptions? _currentOptions;

    /// <inheritdoc />
    protected override void RenderDocument(RenderContext context, Stream output)
    {
        _currentContext = context;
        _currentOptions = context.Options;

        var document = context.Document;
        var state = context.GetOrCreate(() => new HtmlRenderState());
        state.IdTitles = BuildIdTitleMap(document);
        state.TitleIds = BuildTitleIdMap(state.IdTitles);
        state.DocumentAttributes = document.Attributes;
        state.TableCounter = 1;

        var htmlOptions = context.Options as HtmlRenderOptions;
        state.EnableSyntaxHighlighting = htmlOptions?.EnableSyntaxHighlighting ?? false;
        state.EnableIncrementalMarkers = htmlOptions?.EnableIncrementalMarkers ?? false;
        state.DataUriEnabled = document.Attributes.ContainsKey("data-uri");
        state.BaseDirectory = htmlOptions?.BaseDirectory;
        state.ImagesDir = document.Attributes.TryGetValue("imagesdir", out var imgDir) ? imgDir : null;
        bool fullDoc = htmlOptions?.IsFullDocument == true;

        var sb = new StringBuilder();

        if (fullDoc)
            AppendDocumentPrologue(sb, document, htmlOptions!);

        var footnotes = new FootnoteState();

        RenderDocumentBody(sb, document, footnotes, state, fullDoc);
        RenderFootnotesSection(sb, footnotes, state);

        if (fullDoc)
            AppendDocumentEpilogue(sb, document, htmlOptions);

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        output.Write(bytes, 0, bytes.Length);
    }

    // Document prologue/epilogue, BuildIdTitleMap, CollectTitles -> HtmlDocumentRenderer.cs

    private void RenderDocumentBody(StringBuilder sb, DocumentNode document, FootnoteState footnotes, HtmlRenderState state, bool fullDoc = false)
    {
        var secCtx = new SectionNumberingContext(document);

        // In full-document mode, always show title.
        // In embedded mode (FullDocument=false), suppress title unless :showtitle: is set.
        // This matches Asciidoctor's -s behavior: title is rendered only in standalone mode
        // or when :showtitle: is explicitly set in the document.
        // :notitle: suppresses the title entirely.
        bool emitTitle = document.Title is not null
            && !document.Attributes.ContainsKey("notitle")
            && !document.Attributes.ContainsKey("noheader")
            && (fullDoc || document.Attributes.ContainsKey("showtitle"));
        // Determine where the TOC renders. Asciidoctor positions:
        //   - inside #header for default `:toc:` (== :toc: auto), :toc: left,
        //     :toc: right (default placement is "header" on the html5 backend)
        //   - inside #preamble for :toc: preamble
        //   - at toc::[] location for :toc: macro
        // Only the "in header" case is special-cased here. The TocNode is then
        // skipped during regular content rendering.
        var tocPosition = document.Attributes.TryGetValue("toc", out var tocAttrVal)
            ? tocAttrVal.Trim().ToLowerInvariant() : null;
        bool tocInHeader = fullDoc
            && tocPosition is not null
            && tocPosition is "" or "auto" or "left" or "right";
        TocNode? tocNodeForHeader = null;
        if (tocInHeader)
        {
            foreach (var c in document.Children)
                if (c is TocNode t) { tocNodeForHeader = t; break; }
        }

        if (emitTitle)
        {
            // In full-document mode the title is wrapped in <div id="header"> to match
            // Asciidoctor's standalone HTML structure; in embedded mode the bare <h1> is emitted.
            if (fullDoc)
                sb.Append("<div id=\"header\">\n");
            sb.Append("<h1>");
            EscapeTo(sb, document.Title!);
            sb.Append("</h1>\n");
            if (fullDoc)
            {
                // Author / revision details block — rendered when any of the
                // related attributes are set (matches asciidoctor's <div class="details">).
                AppendHeaderDetails(sb, document.Attributes);
                // TOC inside header for :toc: left|right (asciidoctor's class="toc2").
                if (tocNodeForHeader is not null)
                {
                    RenderTocInHeader(sb, tocNodeForHeader, secCtx, state);
                    state.SkipInlineToc = true;
                }
                sb.Append("</div>\n");
            }
        }
        // In full-document mode, wrap the post-header section content in <div id="content">
        // to match Asciidoctor's structure. Closed at the end of RenderDocumentBody.
        if (fullDoc)
            sb.Append("<div id=\"content\">\n");

        bool useIconFont = document.Attributes.TryGetValue("icons", out var iconsValue)
            && string.Equals(iconsValue, "font", StringComparison.OrdinalIgnoreCase);

        // Asciidoctor wraps any non-Section content that precedes the first
        // section in <div id="preamble"><div class="sectionbody">...</div></div>.
        // Only applies in full-document mode (matches asciidoctor's standalone
        // HTML output). Skip the preamble wrapper if the document begins with
        // a section (no preamble content) or if not in full-doc mode.
        int firstSectionIdx = -1;
        if (fullDoc)
        {
            for (int i = 0; i < document.Children.Count; i++)
            {
                if (document.Children[i] is SectionNode { IsDiscrete: false })
                {
                    firstSectionIdx = i;
                    break;
                }
            }
        }
        // The preamble holds children [0..firstSectionIdx) — but the TocNode
        // doesn't count as preamble body (it's rendered in header or at toc::[]).
        // If every child before the first section is a TocNode, there's no real
        // preamble content and the wrapper should be skipped.
        bool preambleHasBody = false;
        if (firstSectionIdx > 0)
        {
            for (int i = 0; i < firstSectionIdx; i++)
            {
                if (document.Children[i] is not TocNode)
                {
                    preambleHasBody = true;
                    break;
                }
            }
        }
        // Only wrap in <div id="preamble"> when there's BOTH:
        // - real (non-TOC) preamble content at the start, AND
        // - at least one section that follows.
        bool hasPreamble = fullDoc
            && firstSectionIdx > 0
            && preambleHasBody;

        if (hasPreamble)
        {
            // The preamble holds children [0..firstSectionIdx) (or all children
            // if no section ever appears). Render those children inside the
            // preamble wrapper, then render the rest normally.
            int preambleEnd = firstSectionIdx == -1 ? document.Children.Count : firstSectionIdx;
            sb.Append("<div id=\"preamble\">\n<div class=\"sectionbody\">\n");
            var preambleChildren = new List<AstNode>(preambleEnd);
            for (int i = 0; i < preambleEnd; i++)
                preambleChildren.Add(document.Children[i]);
            if (state.EnableIncrementalMarkers)
                RenderChildBlocksWithMarkers(sb, preambleChildren, useIconFont, footnotes, secCtx, state);
            else
                RenderChildBlocks(sb, preambleChildren, useIconFont, footnotes, secCtx, state);
            sb.Append("</div>\n</div>\n");

            if (firstSectionIdx >= 0)
            {
                var rest = new List<AstNode>(document.Children.Count - firstSectionIdx);
                for (int i = firstSectionIdx; i < document.Children.Count; i++)
                    rest.Add(document.Children[i]);
                if (state.EnableIncrementalMarkers)
                    RenderChildBlocksWithMarkers(sb, rest, useIconFont, footnotes, secCtx, state);
                else
                    RenderChildBlocks(sb, rest, useIconFont, footnotes, secCtx, state);
            }
        }
        else
        {
            if (state.EnableIncrementalMarkers)
                RenderChildBlocksWithMarkers(sb, document.Children, useIconFont, footnotes, secCtx, state);
            else
                RenderChildBlocks(sb, document.Children, useIconFont, footnotes, secCtx, state);
        }

        // Close the <div id="content"> opened above (full-document mode only).
        if (fullDoc)
            sb.Append("</div>\n");
    }

    private void RenderChildBlocksWithMarkers(StringBuilder sb, IReadOnlyList<AstNode> children, bool useIconFont, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        bool inBibList = false;
        for (int i = 0; i < children.Count; i++)
        {
            sb.Append("<!-- sect:");
            sb.Append(i);
            sb.Append(" -->\n");

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

            sb.Append("<!-- /sect:");
            sb.Append(i);
            sb.Append(" -->\n");
        }
        if (inBibList)
            sb.Append("</ul>\n");
    }

    /// <summary>
    /// Renders the footnotes section at the bottom of the document, if any footnotes were collected.
    /// </summary>
    private void RenderFootnotesSection(StringBuilder sb, FootnoteState footnotes, HtmlRenderState state)
    {
        if (footnotes.Footnotes.Count == 0) return;
        if (state.DocumentAttributes.ContainsKey("nofootnotes")) return;

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

    private bool TryRenderTemplate(StringBuilder sb, AstNode node)
    {
        var templates = (_currentOptions as HtmlRenderOptions)?.Templates;
        if (templates is null) return false;

        foreach (var template in templates)
        {
            if (template.CanRender(node))
            {
                sb.Append(template.Render(node, _currentContext!));
                return true;
            }
        }
        return false;
    }

    private void RenderBlock(StringBuilder sb, AstNode node, bool useIconFont, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        if (TryRenderTemplate(sb, node))
            return;

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
            case StemBlockNode stemBlock:
                RenderStemBlock(sb, stemBlock);
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
                // When the TOC is rendered inside #header (toc: left/right),
                // a state flag suppresses the normal in-content rendering so
                // we don't emit the same TOC twice. State is set in
                // RenderDocumentBody when tocInHeader is true.
                if (state.SkipInlineToc)
                    break;
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

    private static void AppendImageSrc(StringBuilder sb, string target, HtmlRenderState state)
    {
        if (state.DataUriEnabled)
        {
            var dataUri = DataUriHelper.TryConvertToDataUri(target, state.BaseDirectory, state.ImagesDir);
            if (dataUri is not null)
            {
                sb.Append(dataUri);
                return;
            }
        }

        EscapeTo(sb, target);
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

    // RenderHighlightedContent, RenderVerbatimContent, ExpandAttributes -> HtmlBlockRenderer.cs

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
            // Escape &, <, > and smart-punctuation Unicode characters.
            // Asciidoctor outputs all typographic characters as HTML numeric entities
            // rather than raw UTF-8, so we match that behavior.
            string? entity = value[i] switch
            {
                '&'    => "&amp;",
                '<'    => "&lt;",
                '>'    => "&gt;",
                '\u2018' => "&#8216;", // left single quotation mark
                '\u2019' => "&#8217;", // right single quotation mark / apostrophe
                '\u201C' => "&#8220;", // left double quotation mark
                '\u201D' => "&#8221;", // right double quotation mark
                '\u2014' => "&#8212;", // em dash
                '\u2013' => "&#8211;", // en dash
                '\u2026' => "&#8230;", // ellipsis
                '\u2009' => "&#8201;", // thin space
                '\u200B' => "&#8203;", // zero-width space
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

    /// <summary>Strips http:// or https:// prefix from a URL for display purposes.</summary>
    private static string StripUriScheme(string url)
    {
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return url[8..];
        if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            return url[7..];
        return url;
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

    /// <summary>
    /// Emits the &lt;div class="details"&gt; block inside the document header
    /// when any author/email/revision attributes are set. Matches asciidoctor's
    /// standalone HTML output structure exactly.
    /// </summary>
    private static void AppendHeaderDetails(StringBuilder sb, IReadOnlyDictionary<string, string> attrs)
    {
        bool hasAuthor = attrs.TryGetValue("author", out var author) && !string.IsNullOrWhiteSpace(author);
        bool hasEmail = attrs.TryGetValue("email", out var email) && !string.IsNullOrWhiteSpace(email);
        bool hasRevnumber = attrs.TryGetValue("revnumber", out var revnumber) && !string.IsNullOrWhiteSpace(revnumber);
        bool hasRevdate = attrs.TryGetValue("revdate", out var revdate) && !string.IsNullOrWhiteSpace(revdate);
        bool hasRevremark = attrs.TryGetValue("revremark", out var revremark) && !string.IsNullOrWhiteSpace(revremark);
        if (!hasAuthor && !hasRevnumber && !hasRevdate)
            return;

        sb.Append("<div class=\"details\">\n");
        if (hasAuthor)
        {
            sb.Append("<span id=\"author\" class=\"author\">");
            EscapeTo(sb, author!);
            sb.Append("</span><br>\n");
            if (hasEmail)
            {
                sb.Append("<span id=\"email\" class=\"email\"><a href=\"mailto:");
                EscapeTo(sb, email!);
                sb.Append("\">");
                EscapeTo(sb, email!);
                sb.Append("</a></span><br>\n");
            }
        }
        if (hasRevnumber)
        {
            // Asciidoctor's "Version " prefix (capitalised) is locale-aware
            // (en.yml: "version-label: Version"); revdate follows comma-separated.
            sb.Append("<span id=\"revnumber\">Version ");
            EscapeTo(sb, revnumber!);
            if (hasRevdate)
                sb.Append(',');
            sb.Append("</span>\n");
        }
        if (hasRevdate)
        {
            sb.Append("<span id=\"revdate\">");
            EscapeTo(sb, revdate!);
            sb.Append("</span>\n");
        }
        if (hasRevremark)
        {
            sb.Append("<br><span id=\"revremark\">");
            EscapeTo(sb, revremark!);
            sb.Append("</span>\n");
        }
        sb.Append("</div>\n");
    }

    /// <summary>
    /// Renders the TOC inside &lt;div id="header"&gt; using asciidoctor's
    /// class="toc2" (used when :toc: left or :toc: right). The body class is
    /// also augmented with "toc-left toc2" / "toc-right toc2" — that's set in
    /// HtmlDocumentRenderer.AppendDocumentPrologue.
    /// </summary>
    private void RenderTocInHeader(StringBuilder sb, TocNode toc, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        if (toc.Entries.Count == 0) return;
        // Class differs by placement: "toc2" for sidebar layouts (left/right),
        // "toc" for the default in-header placement.
        var tocPos = state.DocumentAttributes.TryGetValue("toc", out var t)
            ? t.Trim().ToLowerInvariant() : null;
        var tocClass = tocPos is "left" or "right" ? "toc2" : "toc";
        sb.Append("<div id=\"toc\" class=\"").Append(tocClass).Append("\">\n");
        var tocTitle = state.DocumentAttributes.TryGetValue("toc-title", out var customTocTitle)
            ? customTocTitle : "Table of Contents";
        sb.Append("<div id=\"toctitle\">");
        EscapeTo(sb, tocTitle);
        sb.Append("</div>\n");
        var tocSecCtx = new SectionNumberingContext(secCtx);
        // RenderTocEntries is in HtmlSectionRenderer (partial class).
        RenderTocEntries(sb, toc.Entries, tocSecCtx);
        sb.Append("</div>\n");
    }
}
