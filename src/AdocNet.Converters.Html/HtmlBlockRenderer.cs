using System.Text;
using System.Text.RegularExpressions;
using AdocNet;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Converters.Html;

public sealed partial class HtmlRenderer
{
    private void RenderParagraph(StringBuilder sb, ParagraphNode paragraph, FootnoteState footnotes, HtmlRenderState state)
    {
        // Asciidoctor always emits a <div class="paragraph"> wrapper around every paragraph.
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
        sb.Append("</div>\n");
    }

    private void RenderDelimitedBlock(StringBuilder sb, DelimitedBlockNode block, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        // Collapsible blocks wrap in <details>/<summary>.
        if (block.IsCollapsible)
        {
            sb.Append("<details>\n<summary class=\"title\">");
            if (block.Title is not null)
            {
                var sumTitleInlines = InlineParser.Parse(block.Title, SubstitutionKind.Normal, state.DocumentAttributes);
                RenderInlines(sb, sumTitleInlines, block.Title, footnotes, state);
            }
            else
                sb.Append("Details");
            sb.Append("</summary>\n<div class=\"content\">\n");
            // Render block content without normal title (already in <summary>).
            RenderDelimitedBlockContent(sb, block, footnotes, secCtx, state);
            sb.Append("</div>\n</details>\n");
            return;
        }

        // Verbatim blocks (Listing, Source, Literal) use an outer wrapper div that must
        // enclose both the title and the inner <div class="content"> wrapper.
        // Asciidoctor: <div class="listingblock"><div class="title">...</div><div class="content"><pre>...</pre></div></div>
        bool isVerbatim = block.BlockKind is DelimitedBlockKind.Listing
            or DelimitedBlockKind.Source
            or DelimitedBlockKind.Literal;

        if (isVerbatim)
        {
            var outerClass = block.BlockKind == DelimitedBlockKind.Literal ? "literalblock" : "listingblock";
            sb.Append("<div");
            AppendRoleClasses(sb, block, outerClass);
            if (block.Id is not null)
            {
                sb.Append(" id=\"");
                EscapeTo(sb, block.Id);
                sb.Append('"');
            }
            sb.Append(">\n");
        }

        // Title (inside the outer div, but before the content wrapper).
        if (block.Title is not null && block.BlockKind != DelimitedBlockKind.Passthrough)
        {
            // Example blocks use a numbered caption ("Example N. Title")
            if (block.BlockKind == DelimitedBlockKind.Example)
            {
                sb.Append("<div class=\"title\">Example ");
                sb.Append(state.ExampleCounter++);
                sb.Append(". ");
                var exTitleInlines = InlineParser.Parse(block.Title, SubstitutionKind.Normal, state.DocumentAttributes);
                RenderInlines(sb, exTitleInlines, block.Title, footnotes, state);
                sb.Append("</div>\n");
            }
            else
            {
                sb.Append("<div class=\"title\">");
                var blkTitleInlines = InlineParser.Parse(block.Title, SubstitutionKind.Normal, state.DocumentAttributes);
                RenderInlines(sb, blkTitleInlines, block.Title, footnotes, state);
                sb.Append("</div>\n");
            }
        }

        RenderDelimitedBlockContent(sb, block, footnotes, secCtx, state);

        if (isVerbatim)
        {
            sb.Append("</div>\n");
            // Callout list is a sibling of the outer block div (Asciidoctor behavior).
            RenderCalloutList(sb, block, footnotes, state);
        }
    }

    private void RenderDelimitedBlockContent(StringBuilder sb, DelimitedBlockNode block, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        switch (block.BlockKind)
        {
            case DelimitedBlockKind.Literal:
            {
                // Outer div emitted by RenderDelimitedBlock(); emit only the inner content wrapper.
                sb.Append("<div class=\"content\">\n");
                sb.Append("<pre>");
                RenderVerbatimContent(sb, block, state);
                sb.Append("</pre>\n");
                sb.Append("</div>\n");
                break;
            }

            case DelimitedBlockKind.Listing:
            {
                // Outer div emitted by RenderDelimitedBlock(); emit only the inner content wrapper.
                sb.Append("<div class=\"content\">\n");
                sb.Append("<pre>");
                RenderVerbatimContent(sb, block, state);
                sb.Append("</pre>\n");
                sb.Append("</div>\n");
                // Note: callout list is rendered by RenderDelimitedBlock() after the outer </div>.
                break;
            }

            case DelimitedBlockKind.Source:
            {
                // Resolve effective language: explicit > :source-language: attribute > null.
                var effectiveLang = block.Language;
                if (effectiveLang is null)
                    state.DocumentAttributes.TryGetValue("source-language", out effectiveLang);

                // Asciidoctor adds highlightjs/hljs classes when source-highlighter is set.
                bool useHighlightJs = state.DocumentAttributes.TryGetValue("source-highlighter", out var highlighter)
                    && highlighter is "highlight.js" or "highlightjs";

                // Outer div emitted by RenderDelimitedBlock(); emit only the inner content wrapper.
                sb.Append("<div class=\"content\">\n");
                sb.Append(useHighlightJs ? "<pre class=\"highlight highlightjs\"><code" : "<pre class=\"highlight\"><code");
                if (effectiveLang is not null)
                {
                    sb.Append(useHighlightJs ? " class=\"hljs language-" : " class=\"language-");
                    EscapeTo(sb, effectiveLang);
                    sb.Append("\" data-lang=\"");
                    EscapeTo(sb, effectiveLang);
                    sb.Append('"');
                }
                sb.Append('>');

                // Use server-side syntax highlighting when available and enabled
                if (!useHighlightJs && state.EnableSyntaxHighlighting
                    && effectiveLang is not null
                    && Highlighting.SyntaxTokenizer.IsLanguageSupported(effectiveLang))
                {
                    RenderHighlightedContent(sb, block);
                }
                else
                {
                    RenderVerbatimContent(sb, block, state);
                }

                sb.Append("</code></pre>\n");
                sb.Append("</div>\n");
                // Note: callout list is rendered by RenderDelimitedBlock() after the outer </div>.
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
                if (block.Content is not null && block.Children.Count == 0)
                {
                    // Paragraph-style quote: render content as inline text (no <p> wrapper).
                    var quoteInlines = InlineParser.Parse(block.Content, block.Substitutions ?? SubstitutionKind.Normal, state.DocumentAttributes);
                    RenderInlines(sb, quoteInlines, block.Content, footnotes, state);
                    sb.Append('\n');
                }
                else
                {
                    foreach (var child in block.Children)
                        RenderBlock(sb, child, false, footnotes, secCtx, state);
                }
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
        if (admonition.Title is not null)
        {
            sb.Append("<div class=\"title\">");
            var titleInlines = InlineParser.Parse(admonition.Title, SubstitutionKind.Normal, state.DocumentAttributes);
            RenderInlines(sb, titleInlines, admonition.Title, footnotes, state);
            sb.Append("</div>\n");
        }
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

    // RenderVideo, RenderAudio -> HtmlImageRenderer.cs
    // RenderIndex -> HtmlDocumentRenderer.cs

    /// <summary>
    /// Renders source block content with server-side syntax highlighting.
    /// Each non-plain token is wrapped in a &lt;span class="hl-XX"&gt; element.
    /// </summary>
    private static void RenderHighlightedContent(StringBuilder sb, DelimitedBlockNode block)
    {
        var content = block.Content ?? string.Empty;
        var tokens = Highlighting.SyntaxTokenizer.Tokenize(content, block.Language);

        foreach (var token in tokens)
        {
            var cssClass = Highlighting.SyntaxTokenizer.GetCssClass(token.Kind);
            if (cssClass is not null)
            {
                sb.Append("<span class=\"");
                sb.Append(cssClass);
                sb.Append("\">");
                EscapeTo(sb, token.Text);
                sb.Append("</span>");
            }
            else
            {
                EscapeTo(sb, token.Text);
            }
        }
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
}
