using System.Text;
using AdocNet.Ast;

namespace AdocNet.Converters.Revealjs;

/// <summary>
/// Renders a <see cref="DocumentNode"/> AST to a reveal.js HTML presentation.
/// Each level-1 section becomes a horizontal slide, level-2 sections become vertical slides.
/// </summary>
public sealed partial class RevealjsRenderer : IDocumentRenderer
{
    private const string DefaultCdnBase = "https://cdn.jsdelivr.net/npm/reveal.js@4/dist";

    // Per-render mutable state. Reset at the start of each Render() call so
    // multiple invocations on the same renderer produce deterministic output.
    private int _exampleCounter;
    private bool _sectnumsEnabled;
    // sectnumlevels: which depths get numbered (default 3). Counters indexed
    // by section level minus one.
    private int[] _sectionCounters = [];
    private int _sectnumLevels;
    private bool _highlightJs;

    /// <inheritdoc />
    public string Format => "revealjs";

    /// <inheritdoc />
    public void Render(DocumentNode document, Stream output, RenderOptions options)
    {
        _exampleCounter = 0;
        _sectnumsEnabled = document.Attributes.ContainsKey("sectnums");
        _sectnumLevels = 3;
        if (document.Attributes.TryGetValue("sectnumlevels", out var lvls)
            && int.TryParse(lvls, out var parsedLvls) && parsedLvls >= 0)
            _sectnumLevels = parsedLvls;
        _sectionCounters = new int[Math.Max(_sectnumLevels, 1)];
        _highlightJs = document.Attributes.TryGetValue("source-highlighter", out var sh)
            && (sh == "highlight.js" || sh == "highlightjs");

        var sb = new StringBuilder();
        RenderPresentation(sb, document);
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        output.Write(bytes, 0, bytes.Length);
    }

    /// <summary>
    /// Splits a document title on the first ": " separator into title + subtitle.
    /// Mirrors Asciidoctor's standalone-document title splitting behaviour so
    /// the title slide can render the subtitle as a separate &lt;h2&gt;.
    /// </summary>
    private static (string Title, string? Subtitle) SplitTitleSubtitle(string fullTitle)
    {
        var idx = fullTitle.IndexOf(": ", StringComparison.Ordinal);
        if (idx < 0) return (fullTitle, null);
        return (fullTitle.Substring(0, idx), fullTitle.Substring(idx + 2));
    }

    /// <summary>
    /// Advances the section counter for <paramref name="level"/> and returns the
    /// numeric prefix (e.g. "1. " or "1.2. "), or null when numbering is off
    /// or the level exceeds <c>:sectnumlevels:</c>.
    /// </summary>
    private string? AdvanceSectionNumber(int level)
    {
        if (!_sectnumsEnabled) return null;
        if (level < 1 || level > _sectnumLevels) return null;
        int idx = level - 1;
        _sectionCounters[idx]++;
        for (int i = idx + 1; i < _sectionCounters.Length; i++)
            _sectionCounters[i] = 0;
        var sb = new StringBuilder();
        for (int i = 0; i <= idx; i++)
        {
            sb.Append(_sectionCounters[i]);
            sb.Append('.');
        }
        sb.Append(' ');
        return sb.ToString();
    }

    private void RenderPresentation(StringBuilder sb, DocumentNode document)
    {
        var theme = GetAttribute(document, "revealjs_theme", "black");
        var transition = GetAttribute(document, "revealjs_transition", "slide");
        var cdnBase = DefaultCdnBase;

        AppendPrologue(sb, document, theme, cdnBase);

        sb.Append("<div class=\"reveal\">\n<div class=\"slides\">\n");

        // Asciidoctor-revealjs emits a dedicated title slide with class="title"
        // and data-state="title" containing the document title, author byline,
        // and any preamble content (BlockNodes that appear before the first
        // top-level SectionNode) wrapped in <div class="preamble">.
        // Subsequent level-1 sections render as their own slides.
        var children = document.Children;
        int firstSectionIdx = children.Count;
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] is SectionNode s && s.Level == 1)
            {
                firstSectionIdx = i;
                break;
            }
        }

        if (document.Title is not null)
        {
            sb.Append("<section class=\"title\" data-state=\"title\">\n<h1>");
            // Asciidoctor splits a "Title: Subtitle" on the first ": " into <h1>+<h2>.
            var (titleText, subtitleText) = SplitTitleSubtitle(document.Title);
            RenderTextAsInlines(sb, titleText);
            sb.Append("</h1>\n");
            if (subtitleText is not null)
            {
                sb.Append("<h2>");
                RenderTextAsInlines(sb, subtitleText);
                sb.Append("</h2>\n");
            }
            if (document.Attributes.TryGetValue("author", out var author) && !string.IsNullOrWhiteSpace(author))
            {
                sb.Append("<p class=\"byline\">\n<span class=\"author\">");
                EscapeTo(sb, author);
                sb.Append("</span>\n</p>\n");
            }

            // Preamble: top-level blocks before the first level-1 section,
            // wrapped in <div class="preamble"> inside the title slide.
            bool wrotePreambleOpen = false;
            for (int i = 0; i < firstSectionIdx; i++)
            {
                var child = children[i];
                if (child is TocNode) continue;
                if (child is BlockNode block)
                {
                    if (!wrotePreambleOpen)
                    {
                        sb.Append("<div class=\"preamble\">\n");
                        wrotePreambleOpen = true;
                    }
                    RenderBlock(sb, block);
                }
            }
            if (wrotePreambleOpen)
                sb.Append("</div>\n");

            sb.Append("</section>\n");
        }
        else
        {
            // No document title — preamble blocks each get their own slide.
            for (int i = 0; i < firstSectionIdx; i++)
            {
                var child = children[i];
                if (child is TocNode) continue;
                if (child is BlockNode block)
                {
                    sb.Append("<section>\n");
                    RenderBlock(sb, block);
                    sb.Append("</section>\n");
                }
            }
        }

        for (int i = firstSectionIdx; i < children.Count; i++)
        {
            var child = children[i];
            if (child is TocNode) continue;
            if (child is SectionNode section && section.Level == 1)
                RenderSlide(sb, section);
            else if (child is BlockNode block)
            {
                // BlockNodes appearing AFTER the first section are unusual but
                // still render as standalone slides (matches prior behaviour).
                sb.Append("<section>\n");
                RenderBlock(sb, block);
                sb.Append("</section>\n");
            }
        }

        sb.Append("</div>\n</div>\n");

        AppendEpilogue(sb, document, transition, cdnBase);
    }

    // ── Prologue / Epilogue ─────────────────────────────────────────────

    private static void AppendPrologue(
        StringBuilder sb, DocumentNode document, string theme, string cdnBase)
    {
        sb.Append("<!DOCTYPE html>\n<html>\n<head>\n");
        sb.Append("<meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">\n");

        sb.Append("<title>");
        EscapeTo(sb, document.Title ?? "Presentation");
        sb.Append("</title>\n");

        sb.Append("<link rel=\"stylesheet\" href=\"");
        sb.Append(cdnBase);
        sb.Append("/reveal.css\">\n");

        sb.Append("<link rel=\"stylesheet\" href=\"");
        sb.Append(cdnBase);
        sb.Append("/theme/");
        EscapeTo(sb, theme);
        sb.Append(".css\">\n");

        // MathJax if :stem: set
        if (document.Attributes.ContainsKey("stem"))
        {
            sb.Append("<script src=\"https://cdn.jsdelivr.net/npm/mathjax@3/es5/tex-mml-chtml.js\"></script>\n");
        }

        sb.Append("</head>\n<body>\n");
    }

    private static void AppendEpilogue(
        StringBuilder sb, DocumentNode document, string transition, string cdnBase)
    {
        sb.Append("<script src=\"");
        sb.Append(cdnBase);
        sb.Append("/reveal.js\"></script>\n");

        sb.Append("<script>\nReveal.initialize({\n");

        sb.Append("  transition: '");
        EscapeTo(sb, transition);
        sb.Append("'");

        AppendBoolConfig(sb, document, "revealjs_controls", "controls");
        AppendBoolConfig(sb, document, "revealjs_progress", "progress");
        AppendBoolConfig(sb, document, "revealjs_center", "center");

        if (document.Attributes.TryGetValue("revealjs_slideNumber", out var slideNum))
        {
            sb.Append(",\n  slideNumber: ");
            if (slideNum is "true" or "false")
                sb.Append(slideNum);
            else
            {
                sb.Append('\'');
                EscapeTo(sb, slideNum);
                sb.Append('\'');
            }
        }

        if (document.Attributes.TryGetValue("revealjs_width", out var width))
        {
            sb.Append(",\n  width: ");
            sb.Append(width);
        }

        if (document.Attributes.TryGetValue("revealjs_height", out var height))
        {
            sb.Append(",\n  height: ");
            sb.Append(height);
        }

        sb.Append('\n');
        sb.Append("});\n</script>\n");
        sb.Append("</body>\n</html>\n");
    }

    private static void AppendBoolConfig(
        StringBuilder sb, DocumentNode document, string attrName, string jsName)
    {
        if (document.Attributes.TryGetValue(attrName, out var val))
        {
            sb.Append(",\n  ");
            sb.Append(jsName);
            sb.Append(": ");
            sb.Append(string.Equals(val, "false", StringComparison.OrdinalIgnoreCase)
                ? "false" : "true");
        }
    }

    // ── Slide rendering ─────────────────────────────────────────────────

    private void RenderSlide(StringBuilder sb, SectionNode section)
    {
        bool hasVerticalSlides = false;
        foreach (var child in section.Children)
        {
            if (child is SectionNode sub && sub.Level == 2)
            {
                hasVerticalSlides = true;
                break;
            }
        }

        if (hasVerticalSlides)
        {
            // Vertical slide group — outer <section> wraps all the verticals.
            // Asciidoctor doesn't put an id on the outer wrapper, only on the
            // inner slides.
            sb.Append("<section>\n");

            // Parent slide with title + non-section content
            AppendSlideOpenTag(sb, section);
            sb.Append("<h2>");
            RenderSectionTitle(sb, section);
            sb.Append("</h2>\n");
            AppendSlideContent(sb, section.Children, stopAtSubsection: true);
            sb.Append("</section>\n");

            // Vertical slides — Asciidoctor uses <h2> for vertical slides too
            // (they live at the same hierarchy as horizontal slides).
            foreach (var child in section.Children)
            {
                if (child is SectionNode sub && sub.Level == 2)
                {
                    AppendSlideOpenTag(sb, sub);
                    sb.Append("<h2>");
                    RenderSectionTitle(sb, sub);
                    sb.Append("</h2>\n");
                    AppendSlideContent(sb, sub.Children, stopAtSubsection: false);
                    sb.Append("</section>\n");
                }
            }

            sb.Append("</section>\n");
        }
        else
        {
            // Simple horizontal slide
            AppendSlideOpenTag(sb, section);
            sb.Append("<h2>");
            RenderSectionTitle(sb, section);
            sb.Append("</h2>\n");
            AppendSlideContent(sb, section.Children, stopAtSubsection: false);
            sb.Append("</section>\n");
        }
    }

    /// <summary>
    /// Wraps slide body content in &lt;div class="slide-content"&gt; so reveal.js
    /// theme CSS can scroll/scale it independently of the heading.
    /// Skipped when there's no body content (heading-only slide).
    /// </summary>
    private void AppendSlideContent(StringBuilder sb, IEnumerable<AstNode> children, bool stopAtSubsection)
    {
        // Pre-check: any block content?
        bool hasContent = false;
        foreach (var child in children)
        {
            if (stopAtSubsection && child is SectionNode) break;
            if (child is BlockNode) { hasContent = true; break; }
        }
        if (!hasContent) return;

        sb.Append("<div class=\"slide-content\">\n");
        foreach (var child in children)
        {
            if (stopAtSubsection && child is SectionNode) break;
            if (child is BlockNode block)
                RenderBlock(sb, block);
        }
        sb.Append("</div>\n");
    }

    private static void AppendSlideOpenTag(StringBuilder sb, SectionNode section)
    {
        sb.Append("<section");
        var id = section.Id;
        if (!string.IsNullOrEmpty(id))
        {
            sb.Append(" id=\"");
            EscapeTo(sb, id!);
            sb.Append('"');
        }
        sb.Append(">\n");
    }

    // ── Block rendering ─────────────────────────────────────────────────

    private void RenderBlock(StringBuilder sb, BlockNode node)
    {
        switch (node)
        {
            case ParagraphNode n: RenderParagraph(sb, n); break;
            case ListNode n: RenderList(sb, n); break;
            case DelimitedBlockNode n: RenderDelimitedBlock(sb, n); break;
            case BlockImageNode n: RenderBlockImage(sb, n); break;
            case AdmonitionNode n: RenderAdmonition(sb, n); break;
            case TableNode n: RenderTable(sb, n); break;
            case StemBlockNode n: RenderStemBlock(sb, n); break;
            case DescriptionListNode n: RenderDescriptionList(sb, n); break;
            case SectionNode n:
                // Deeper sections rendered as headings within the slide.
                // Asciidoctor's reveal.js converter maps level N → <h{N}>:
                // level 3 → <h3>, level 4 → <h4>, level 5 → <h5>.
                var tag = n.Level switch { 3 => "h3", 4 => "h4", 5 => "h5", _ => "h6" };
                sb.Append('<').Append(tag).Append('>');
                RenderSectionTitle(sb, n);
                sb.Append("</").Append(tag).Append(">\n");
                foreach (var child in n.Children)
                    if (child is BlockNode block)
                        RenderBlock(sb, block);
                break;
            default: break;
        }
    }

    private static void RenderParagraph(StringBuilder sb, ParagraphNode paragraph)
    {
        // Asciidoctor wraps every paragraph in <div class="paragraph">.
        sb.Append("<div class=\"paragraph\">\n<p>");
        if (paragraph.Inlines.Count > 0)
            RenderInlines(sb, paragraph.Inlines);
        else
            EscapeTo(sb, paragraph.Text);
        sb.Append("</p>\n</div>\n");
    }

    private void RenderList(StringBuilder sb, ListNode list)
    {
        // Asciidoctor wraps lists in <div class="ulist"> or "olist".
        var listClass = list.ListKind == ListKind.Ordered ? "olist" : "ulist";
        var tag = list.ListKind == ListKind.Ordered ? "ol" : "ul";
        sb.Append("<div class=\"").Append(listClass).Append("\">\n");
        sb.Append('<').Append(tag).Append(">\n");
        foreach (var child in list.Children)
        {
            if (child is ListItemNode item)
            {
                // Asciidoctor wraps list-item text in <p> for consistent paragraph
                // styling across nested content.
                sb.Append("<li>\n<p>");
                if (item.Inlines.Count > 0)
                    RenderInlines(sb, item.Inlines);
                else
                    EscapeTo(sb, item.Text);
                sb.Append("</p>\n");
                // Recurse into list-item children (nested lists, list continuations).
                foreach (var child2 in item.Children)
                    if (child2 is BlockNode b) RenderBlock(sb, b);
                sb.Append("</li>\n");
            }
        }
        sb.Append("</").Append(tag).Append(">\n");
        sb.Append("</div>\n");
    }

    private void RenderDelimitedBlock(StringBuilder sb, DelimitedBlockNode block)
    {
        // Speaker notes: [.notes] role
        if (block.Roles.Contains("notes"))
        {
            sb.Append("<aside class=\"notes\">\n");
            if (block.Content is not null)
            {
                sb.Append("<p>");
                EscapeTo(sb, block.Content);
                sb.Append("</p>\n");
            }
            foreach (var child in block.Children)
                if (child is BlockNode b) RenderBlock(sb, b);
            sb.Append("</aside>\n");
            return;
        }

        switch (block.BlockKind)
        {
            case DelimitedBlockKind.Source:
            case DelimitedBlockKind.Listing:
                AppendListingBlock(sb, block);
                break;

            case DelimitedBlockKind.Literal:
                sb.Append("<div class=\"literalblock\">\n");
                AppendOptionalTitle(sb, block.Title);
                sb.Append("<div class=\"content\">\n<pre>");
                EscapeTo(sb, block.Content ?? "");
                sb.Append("</pre>\n</div>\n</div>\n");
                break;

            case DelimitedBlockKind.Quote:
                sb.Append("<div class=\"quoteblock\">\n");
                AppendOptionalTitle(sb, block.Title);
                sb.Append("<blockquote>\n");
                foreach (var child in block.Children)
                    if (child is BlockNode b) RenderBlock(sb, b);
                sb.Append("</blockquote>\n");
                if (block.Attribution is not null)
                {
                    sb.Append("<div class=\"attribution\">&#8212; ");
                    EscapeTo(sb, block.Attribution);
                    sb.Append("</div>\n");
                }
                sb.Append("</div>\n");
                break;

            case DelimitedBlockKind.Example:
                // Asciidoctor wraps example blocks in <div class="exampleblock">.
                // Titled examples receive a numbered prefix ("Example N. <title>").
                sb.Append("<div class=\"exampleblock\"");
                if (block.Id is not null)
                {
                    sb.Append(" id=\"");
                    EscapeTo(sb, block.Id);
                    sb.Append('"');
                }
                sb.Append(">\n");
                if (block.Title is not null)
                {
                    sb.Append("<div class=\"title\">Example ");
                    sb.Append(++_exampleCounter);
                    sb.Append(". ");
                    RenderTextAsInlines(sb, block.Title);
                    sb.Append("</div>\n");
                }
                sb.Append("<div class=\"content\">\n");
                foreach (var child in block.Children)
                    if (child is BlockNode b) RenderBlock(sb, b);
                sb.Append("</div>\n</div>\n");
                break;

            case DelimitedBlockKind.Sidebar:
                sb.Append("<div class=\"sidebarblock\"");
                if (block.Id is not null)
                {
                    sb.Append(" id=\"");
                    EscapeTo(sb, block.Id);
                    sb.Append('"');
                }
                sb.Append(">\n<div class=\"content\">\n");
                if (block.Title is not null)
                {
                    sb.Append("<div class=\"title\">");
                    RenderTextAsInlines(sb, block.Title);
                    sb.Append("</div>\n");
                }
                foreach (var child in block.Children)
                    if (child is BlockNode b) RenderBlock(sb, b);
                sb.Append("</div>\n</div>\n");
                break;

            default:
                // Open block etc.: pass through children with no wrapper.
                foreach (var child in block.Children)
                    if (child is BlockNode b) RenderBlock(sb, b);
                break;
        }
    }

    private void AppendListingBlock(StringBuilder sb, DelimitedBlockNode block)
    {
        sb.Append("<div class=\"listingblock\"");
        if (block.Id is not null)
        {
            sb.Append(" id=\"");
            EscapeTo(sb, block.Id);
            sb.Append('"');
        }
        sb.Append(">\n");
        AppendOptionalTitle(sb, block.Title);
        sb.Append("<div class=\"content\">\n");

        // Build line-number → callout-numbers map for inline conum markers.
        Dictionary<int, List<int>>? conumMap = null;
        if (block.Callouts is { Count: > 0 })
        {
            foreach (var entry in block.Callouts)
            {
                if (entry.LineNumber < 0) continue;
                conumMap ??= new();
                if (!conumMap.TryGetValue(entry.LineNumber, out var nums))
                {
                    nums = new();
                    conumMap[entry.LineNumber] = nums;
                }
                nums.Add(entry.Number);
            }
        }

        if (block.BlockKind == DelimitedBlockKind.Source)
        {
            // :source-highlighter: highlight.js → add 'highlightjs' to <pre>
            // and 'hljs' + data-noescape to <code> (matches Asciidoctor's hint
            // markup for client-side syntax highlighting).
            sb.Append(_highlightJs ? "<pre class=\"highlight highlightjs\"><code" : "<pre class=\"highlight\"><code");
            if (block.Language is not null)
            {
                sb.Append(_highlightJs ? " class=\"hljs language-" : " class=\"language-");
                EscapeTo(sb, block.Language);
                sb.Append("\" data-lang=\"");
                EscapeTo(sb, block.Language);
                sb.Append('"');
            }
            if (_highlightJs)
                sb.Append(" data-noescape=\"true\"");
            sb.Append('>');
            AppendVerbatimWithConums(sb, block.Content ?? "", conumMap);
            sb.Append("</code></pre>\n");
        }
        else
        {
            sb.Append("<pre>");
            AppendVerbatimWithConums(sb, block.Content ?? "", conumMap);
            sb.Append("</pre>\n");
        }
        sb.Append("</div>\n</div>\n");

        // Callout list: <div class="arabic colist"><ol>... after the listing.
        // Asciidoctor's reveal.js converter uses 'arabic colist' (note order)
        // and renders only when entries have explanation text.
        if (block.Callouts is { Count: > 0 } && block.Callouts.Any(e => e.Text.Length > 0 || e.Inlines.Count > 0))
        {
            sb.Append("<div class=\"arabic colist\">\n<ol>\n");
            foreach (var entry in block.Callouts)
            {
                sb.Append("<li>\n<p>");
                if (entry.Inlines.Count > 0)
                    RenderInlines(sb, entry.Inlines);
                else
                    EscapeTo(sb, entry.Text);
                sb.Append("</p>\n</li>\n");
            }
            sb.Append("</ol>\n</div>\n");
        }
    }

    /// <summary>
    /// Writes verbatim content line by line, appending &lt;b&gt;(N)&lt;/b&gt; markers
    /// after each line that has callout markers (matching Asciidoctor's reveal.js
    /// output, which uses bare &lt;b&gt; rather than &lt;b class=\"conum\"&gt;).
    /// When <paramref name="conumMap"/> is null, falls back to a single
    /// EscapeTo of the whole content.
    /// </summary>
    private static void AppendVerbatimWithConums(StringBuilder sb, string content, Dictionary<int, List<int>>? conumMap)
    {
        if (conumMap is null)
        {
            EscapeTo(sb, content);
            return;
        }
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            EscapeTo(sb, lines[i]);
            if (conumMap.TryGetValue(i, out var nums))
            {
                foreach (var num in nums)
                {
                    sb.Append(" <b>(");
                    sb.Append(num);
                    sb.Append(")</b>");
                }
            }
            if (i < lines.Length - 1)
                sb.Append('\n');
        }
    }

    private void AppendOptionalTitle(StringBuilder sb, string? title)
    {
        if (title is null) return;
        sb.Append("<div class=\"title\">");
        RenderTextAsInlines(sb, title);
        sb.Append("</div>\n");
    }

    private static void RenderBlockImage(StringBuilder sb, BlockImageNode image)
    {
        sb.Append("<img src=\"");
        EscapeTo(sb, image.Target);
        sb.Append("\" alt=\"");
        EscapeTo(sb, image.Alt);
        sb.Append("\">\n");
    }

    private void RenderAdmonition(StringBuilder sb, AdmonitionNode admonition)
    {
        // Asciidoctor's admonition is a 2-column table: icon cell + content cell.
        // The icon cell holds <div class="title">Note</div> (title-case).
        var typeLower = admonition.AdmonitionType.ToLowerInvariant();
        var typeTitle = char.ToUpperInvariant(admonition.AdmonitionType[0])
                        + admonition.AdmonitionType.Substring(1).ToLowerInvariant();

        sb.Append("<div class=\"admonitionblock ");
        sb.Append(typeLower);
        sb.Append("\">\n<table>\n<tr>\n<td class=\"icon\">\n<div class=\"title\">");
        sb.Append(typeTitle);
        sb.Append("</div>\n</td>\n<td class=\"content\">\n");
        if (admonition.Inlines.Count > 0)
            RenderInlines(sb, admonition.Inlines);
        else if (admonition.Text is not null)
            EscapeTo(sb, admonition.Text);
        // Render any child blocks (multi-paragraph admonitions) inside the content cell.
        foreach (var child in admonition.Children)
            if (child is BlockNode b) RenderBlock(sb, b);
        sb.Append("\n</td>\n</tr>\n</table>\n</div>\n");
    }

    private void RenderTable(StringBuilder sb, TableNode table)
    {
        sb.Append("<table>\n");
        foreach (var child in table.Children)
        {
            if (child is TableRowNode row)
            {
                sb.Append("<tr>\n");
                foreach (var cell in row.Children)
                {
                    if (cell is TableCellNode cellNode)
                    {
                        sb.Append("<td>");
                        EscapeTo(sb, cellNode.Text);
                        sb.Append("</td>\n");
                    }
                }
                sb.Append("</tr>\n");
            }
        }
        sb.Append("</table>\n");
    }

    private static void RenderStemBlock(StringBuilder sb, StemBlockNode stem)
    {
        sb.Append("<div class=\"stemblock\">\n");
        if (string.Equals(stem.StemType, "asciimath", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("\\$");
            sb.Append(stem.Content);
            sb.Append("\\$");
        }
        else
        {
            sb.Append("\\[");
            sb.Append(stem.Content);
            sb.Append("\\]");
        }
        sb.Append("\n</div>\n");
    }

    private void RenderDescriptionList(StringBuilder sb, DescriptionListNode list)
    {
        // Asciidoctor wraps the list in <div class="dlist"> with the <dl> inside.
        // Each term gets <dt class="hdlist1"> and the description sits in <dd>
        // with the inline text wrapped in <p>. Multi-term entries emit one <dt>
        // per term. Nested block content (continuation paragraphs, sub-lists)
        // renders after the <p>, before </dd>.
        sb.Append("<div class=\"dlist\">\n<dl>\n");
        foreach (var child in list.Children)
        {
            if (child is DescriptionItemNode item)
            {
                // Render every term: prefer AllTermInlines (multi-term entries)
                // and fall back to TermInlines / Terms[0].
                if (item.AllTermInlines is { Count: > 0 })
                {
                    foreach (var termInlines in item.AllTermInlines)
                    {
                        sb.Append("<dt class=\"hdlist1\">");
                        if (termInlines.Count > 0)
                            RenderInlines(sb, termInlines);
                        sb.Append("</dt>\n");
                    }
                }
                else
                {
                    sb.Append("<dt class=\"hdlist1\">");
                    if (item.TermInlines.Count > 0)
                        RenderInlines(sb, item.TermInlines);
                    else if (item.Terms.Count > 0)
                        EscapeTo(sb, item.Terms[0]);
                    sb.Append("</dt>\n");
                }

                sb.Append("<dd>\n");
                bool hasInlineDescription = item.DescriptionInlines.Count > 0
                                            || !string.IsNullOrEmpty(item.Description);
                if (hasInlineDescription)
                {
                    sb.Append("<p>");
                    if (item.DescriptionInlines.Count > 0)
                        RenderInlines(sb, item.DescriptionInlines);
                    else
                        EscapeTo(sb, item.Description);
                    sb.Append("</p>\n");
                }
                // Continuation blocks (nested paragraphs, lists, source blocks…)
                foreach (var nested in item.Children)
                    if (nested is BlockNode b) RenderBlock(sb, b);
                sb.Append("</dd>\n");
            }
        }
        sb.Append("</dl>\n</div>\n");
    }

    // Inline rendering, utilities -> RevealjsRendererInlines.cs
}
