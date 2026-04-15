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

    /// <inheritdoc />
    public string Format => "revealjs";

    /// <inheritdoc />
    public void Render(DocumentNode document, Stream output, RenderOptions options)
    {
        var sb = new StringBuilder();
        RenderPresentation(sb, document);
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        output.Write(bytes, 0, bytes.Length);
    }

    private static void RenderPresentation(StringBuilder sb, DocumentNode document)
    {
        var theme = GetAttribute(document, "revealjs_theme", "black");
        var transition = GetAttribute(document, "revealjs_transition", "slide");
        var cdnBase = DefaultCdnBase;

        AppendPrologue(sb, document, theme, cdnBase);

        sb.Append("<div class=\"reveal\">\n<div class=\"slides\">\n");

        // Title slide from document title
        if (document.Title is not null)
        {
            sb.Append("<section>\n<h1>");
            EscapeTo(sb, document.Title);
            sb.Append("</h1>\n</section>\n");
        }

        // Render top-level children as slides
        foreach (var child in document.Children)
        {
            if (child is SectionNode section && section.Level == 1)
                RenderSlide(sb, section);
            else if (child is BlockNode block)
            {
                // Non-section content → standalone slide
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

    private static void RenderSlide(StringBuilder sb, SectionNode section)
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
            // Vertical slide group
            sb.Append("<section>\n");

            // Parent slide with title + non-section content
            sb.Append("<section>\n<h2>");
            EscapeTo(sb, section.Title);
            sb.Append("</h2>\n");
            foreach (var child in section.Children)
            {
                if (child is SectionNode) break; // stop at first subsection
                if (child is BlockNode block)
                    RenderBlock(sb, block);
            }
            sb.Append("</section>\n");

            // Vertical slides
            foreach (var child in section.Children)
            {
                if (child is SectionNode sub && sub.Level == 2)
                {
                    sb.Append("<section>\n<h3>");
                    EscapeTo(sb, sub.Title);
                    sb.Append("</h3>\n");
                    foreach (var subChild in sub.Children)
                    {
                        if (subChild is BlockNode block)
                            RenderBlock(sb, block);
                    }
                    sb.Append("</section>\n");
                }
            }

            sb.Append("</section>\n");
        }
        else
        {
            // Simple horizontal slide
            sb.Append("<section>\n<h2>");
            EscapeTo(sb, section.Title);
            sb.Append("</h2>\n");
            foreach (var child in section.Children)
            {
                if (child is BlockNode block)
                    RenderBlock(sb, block);
            }
            sb.Append("</section>\n");
        }
    }

    // ── Block rendering ─────────────────────────────────────────────────

    private static void RenderBlock(StringBuilder sb, BlockNode node)
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
                // Deeper sections rendered as headings within the slide
                var tag = n.Level switch { 3 => "h4", 4 => "h5", _ => "h6" };
                sb.Append('<').Append(tag).Append('>');
                EscapeTo(sb, n.Title);
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
        sb.Append("<p>");
        if (paragraph.Inlines.Count > 0)
            RenderInlines(sb, paragraph.Inlines);
        else
            EscapeTo(sb, paragraph.Text);
        sb.Append("</p>\n");
    }

    private static void RenderList(StringBuilder sb, ListNode list)
    {
        var tag = list.ListKind == ListKind.Ordered ? "ol" : "ul";
        sb.Append('<').Append(tag).Append(">\n");
        foreach (var child in list.Children)
        {
            if (child is ListItemNode item)
            {
                sb.Append("<li>");
                if (item.Inlines.Count > 0)
                    RenderInlines(sb, item.Inlines);
                else
                    EscapeTo(sb, item.Text);
                sb.Append("</li>\n");
            }
        }
        sb.Append("</").Append(tag).Append(">\n");
    }

    private static void RenderDelimitedBlock(StringBuilder sb, DelimitedBlockNode block)
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
                sb.Append("<pre><code");
                if (block.Language is not null)
                {
                    sb.Append(" class=\"language-");
                    EscapeTo(sb, block.Language);
                    sb.Append('"');
                }
                sb.Append('>');
                EscapeTo(sb, block.Content ?? "");
                sb.Append("</code></pre>\n");
                break;

            case DelimitedBlockKind.Listing:
            case DelimitedBlockKind.Literal:
                sb.Append("<pre>");
                EscapeTo(sb, block.Content ?? "");
                sb.Append("</pre>\n");
                break;

            case DelimitedBlockKind.Quote:
                sb.Append("<blockquote>\n");
                foreach (var child in block.Children)
                    if (child is BlockNode b) RenderBlock(sb, b);
                if (block.Attribution is not null)
                {
                    sb.Append("<footer>");
                    EscapeTo(sb, block.Attribution);
                    sb.Append("</footer>\n");
                }
                sb.Append("</blockquote>\n");
                break;

            default:
                foreach (var child in block.Children)
                    if (child is BlockNode b) RenderBlock(sb, b);
                break;
        }
    }

    private static void RenderBlockImage(StringBuilder sb, BlockImageNode image)
    {
        sb.Append("<img src=\"");
        EscapeTo(sb, image.Target);
        sb.Append("\" alt=\"");
        EscapeTo(sb, image.Alt);
        sb.Append("\">\n");
    }

    private static void RenderAdmonition(StringBuilder sb, AdmonitionNode admonition)
    {
        sb.Append("<div class=\"admonition ");
        sb.Append(admonition.AdmonitionType.ToLowerInvariant());
        sb.Append("\">\n<strong>");
        EscapeTo(sb, admonition.AdmonitionType);
        sb.Append(":</strong> ");
        if (admonition.Inlines.Count > 0)
            RenderInlines(sb, admonition.Inlines);
        else if (admonition.Text is not null)
            EscapeTo(sb, admonition.Text);
        sb.Append("\n</div>\n");
    }

    private static void RenderTable(StringBuilder sb, TableNode table)
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

    private static void RenderDescriptionList(StringBuilder sb, DescriptionListNode list)
    {
        sb.Append("<dl>\n");
        foreach (var child in list.Children)
        {
            if (child is DescriptionItemNode item)
            {
                sb.Append("<dt>");
                EscapeTo(sb, item.Terms[0]);
                sb.Append("</dt>\n<dd>");
                EscapeTo(sb, item.Description);
                sb.Append("</dd>\n");
            }
        }
        sb.Append("</dl>\n");
    }

    // Inline rendering, utilities -> RevealjsRendererInlines.cs
}
