using System.Text;
using AdocNet.Ast;

namespace AdocNet.Converters.Man;

/// <summary>
/// Renders a <see cref="DocumentNode"/> AST to roff-format man page output.
/// Output uses <c>\n</c> line endings for cross-platform determinism.
/// </summary>
public sealed partial class ManRenderer : IDocumentRenderer
{
    /// <inheritdoc />
    public string Format => "man";

    /// <inheritdoc />
    public void Render(DocumentNode document, Stream output, RenderOptions options)
    {
        var sb = new StringBuilder();
        RenderDocument(sb, document);
        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        output.Write(bytes, 0, bytes.Length);
    }

    private static void RenderDocument(StringBuilder sb, DocumentNode document)
    {
        RenderTitleHeader(sb, document);

        foreach (var child in document.Children)
        {
            if (child is BlockNode block)
                RenderBlock(sb, block);
        }
    }

    // ── Title header ────────────────────────────────────────────────────

    private static void RenderTitleHeader(StringBuilder sb, DocumentNode document)
    {
        var name = document.Title?.ToUpperInvariant() ?? "UNTITLED";
        var section = "1";
        var date = "";
        var source = "";
        var manual = "";

        if (document.Attributes.TryGetValue("mansource", out var src))
            source = src;
        if (document.Attributes.TryGetValue("manmanual", out var man))
            manual = man;
        if (document.Attributes.TryGetValue("revdate", out var rd))
            date = rd;

        // Parse manpage title format: NAME(section)
        if (document.Title is not null)
        {
            var title = document.Title;
            var parenIdx = title.LastIndexOf('(');
            if (parenIdx > 0 && title.EndsWith(")"))
            {
                name = title.Substring(0, parenIdx).Trim().ToUpperInvariant();
                section = title.Substring(parenIdx + 1, title.Length - parenIdx - 2);
            }
            else
            {
                name = title.ToUpperInvariant();
            }
        }

        sb.Append(".TH \"");
        sb.Append(EscapeRoff(name));
        sb.Append("\" \"");
        sb.Append(EscapeRoff(section));
        sb.Append("\" \"");
        sb.Append(EscapeRoff(date));
        sb.Append("\" \"");
        sb.Append(EscapeRoff(source));
        sb.Append("\" \"");
        sb.Append(EscapeRoff(manual));
        sb.Append("\"\n");
    }

    // ── Block rendering ─────────────────────────────────────────────────

    private static void RenderBlock(StringBuilder sb, BlockNode node)
    {
        switch (node)
        {
            case SectionNode n: RenderSection(sb, n); break;
            case ParagraphNode n: RenderParagraph(sb, n); break;
            case ListNode n: RenderList(sb, n); break;
            case DescriptionListNode n: RenderDescriptionList(sb, n); break;
            case DelimitedBlockNode n: RenderDelimitedBlock(sb, n); break;
            case AdmonitionNode n: RenderAdmonition(sb, n); break;
            case BlockImageNode n: RenderBlockImage(sb, n); break;
            case TableNode n: RenderTable(sb, n); break;
            case StemBlockNode n: RenderStemBlock(sb, n); break;
            case PageBreakNode: sb.Append(".bp\n"); break;
            case ThematicBreakNode: break; // No roff equivalent
            case TocNode: break; // Man pages don't have TOC
            case VideoNode: break; // Not renderable in man
            case AudioNode: break; // Not renderable in man
            case IndexNode: break;
            case BibliographyEntryNode n: RenderBibliographyEntry(sb, n); break;
            default: break;
        }
    }

    private static void RenderSection(StringBuilder sb, SectionNode section)
    {
        if (section.Level == 1)
        {
            sb.Append(".SH ");
            sb.Append(GetSectionTitleText(section).ToUpperInvariant());
            sb.Append('\n');
        }
        else if (section.Level == 2)
        {
            sb.Append(".SS ");
            sb.Append(GetSectionTitleText(section));
            sb.Append('\n');
        }
        else
        {
            // Level 3+: bold paragraph (no deeper nesting in roff)
            sb.Append(".PP\n\\fB");
            sb.Append(EscapeBodyText(GetSectionTitleText(section)));
            sb.Append("\\fR\n");
        }

        foreach (var child in section.Children)
        {
            if (child is BlockNode block)
                RenderBlock(sb, block);
        }
    }

    private static string GetSectionTitleText(SectionNode section)
    {
        if (section.TitleInlines.Count > 0)
            return GetInlinesPlainText(section.TitleInlines);
        return section.Title;
    }

    private static void RenderParagraph(StringBuilder sb, ParagraphNode paragraph)
    {
        sb.Append(".PP\n");
        if (paragraph.Inlines.Count > 0)
        {
            RenderInlines(sb, paragraph.Inlines);
            sb.Append('\n');
        }
        else
        {
            sb.Append(EscapeBodyText(paragraph.Text));
            sb.Append('\n');
        }
    }

    private static void RenderList(StringBuilder sb, ListNode list)
    {
        int itemNumber = list.Start ?? 1;
        foreach (var child in list.Children)
        {
            if (child is ListItemNode item)
            {
                if (list.ListKind == ListKind.Ordered)
                {
                    sb.Append(".IP \"");
                    sb.Append(itemNumber);
                    sb.Append(".\" 3\n");
                    itemNumber++;
                }
                else
                {
                    sb.Append(".IP \"\\(bu\" 2\n");
                }

                if (item.Inlines.Count > 0)
                {
                    RenderInlines(sb, item.Inlines);
                    sb.Append('\n');
                }
                else
                {
                    sb.Append(EscapeBodyText(item.Text));
                    sb.Append('\n');
                }

                // Nested blocks (e.g., nested lists)
                foreach (var nested in item.Children)
                {
                    if (nested is BlockNode nestedBlock)
                    {
                        sb.Append(".RS\n");
                        RenderBlock(sb, nestedBlock);
                        sb.Append(".RE\n");
                    }
                }
            }
        }
    }

    private static void RenderDescriptionList(StringBuilder sb, DescriptionListNode list)
    {
        foreach (var child in list.Children)
        {
            if (child is DescriptionItemNode item)
            {
                sb.Append(".TP\n\\fB");
                if (item.TermInlines.Count > 0)
                    RenderInlines(sb, item.TermInlines);
                else
                    sb.Append(EscapeBodyText(item.Term));
                sb.Append("\\fR\n");

                if (item.DescriptionInlines.Count > 0)
                {
                    RenderInlines(sb, item.DescriptionInlines);
                    sb.Append('\n');
                }
                else
                {
                    sb.Append(EscapeBodyText(item.Description));
                    sb.Append('\n');
                }

                // Block children within description item
                foreach (var nested in item.Children)
                {
                    if (nested is BlockNode nestedBlock)
                        RenderBlock(sb, nestedBlock);
                }
            }
        }
    }

    private static void RenderDelimitedBlock(StringBuilder sb, DelimitedBlockNode block)
    {
        switch (block.BlockKind)
        {
            case DelimitedBlockKind.Source:
            case DelimitedBlockKind.Listing:
            case DelimitedBlockKind.Literal:
                if (block.Title is not null)
                {
                    sb.Append(".PP\n\\fB");
                    sb.Append(EscapeBodyText(block.Title));
                    sb.Append("\\fR\n");
                }
                sb.Append(".nf\n");
                sb.Append(EscapeBodyText(block.Content ?? ""));
                sb.Append('\n');
                sb.Append(".fi\n");
                break;

            case DelimitedBlockKind.Quote:
                sb.Append(".RS\n");
                if (block.Content is not null)
                {
                    sb.Append(".PP\n");
                    sb.Append(EscapeBodyText(block.Content));
                    sb.Append('\n');
                }
                foreach (var child in block.Children)
                {
                    if (child is BlockNode childBlock)
                        RenderBlock(sb, childBlock);
                }
                if (block.Attribution is not null)
                {
                    sb.Append(".PP\n\\(em ");
                    sb.Append(EscapeBodyText(block.Attribution));
                    sb.Append('\n');
                }
                sb.Append(".RE\n");
                break;

            case DelimitedBlockKind.Example:
            case DelimitedBlockKind.Sidebar:
            case DelimitedBlockKind.Open:
                if (block.Title is not null)
                {
                    sb.Append(".PP\n\\fB");
                    sb.Append(EscapeBodyText(block.Title));
                    sb.Append("\\fR\n");
                }
                sb.Append(".RS\n");
                foreach (var child in block.Children)
                {
                    if (child is BlockNode childBlock)
                        RenderBlock(sb, childBlock);
                }
                sb.Append(".RE\n");
                break;

            case DelimitedBlockKind.Verse:
                sb.Append(".nf\n");
                sb.Append(EscapeBodyText(block.Content ?? ""));
                sb.Append('\n');
                sb.Append(".fi\n");
                break;

            case DelimitedBlockKind.Passthrough:
                if (block.Content is not null)
                    sb.Append(block.Content);
                break;
        }
    }

    private static void RenderAdmonition(StringBuilder sb, AdmonitionNode admonition)
    {
        sb.Append(".PP\n\\fB");
        sb.Append(admonition.AdmonitionType.ToUpperInvariant());
        sb.Append(":\\fR ");
        if (admonition.Inlines.Count > 0)
        {
            RenderInlines(sb, admonition.Inlines);
            sb.Append('\n');
        }
        else if (admonition.Text is not null)
        {
            sb.Append(EscapeBodyText(admonition.Text));
            sb.Append('\n');
        }
        else
        {
            sb.Append('\n');
        }

        foreach (var child in admonition.Children)
        {
            if (child is BlockNode block)
                RenderBlock(sb, block);
        }
    }

    private static void RenderBlockImage(StringBuilder sb, BlockImageNode image)
    {
        sb.Append(".PP\n[Image: ");
        sb.Append(EscapeBodyText(image.Alt.Length > 0 ? image.Alt : image.Target));
        sb.Append(" (");
        sb.Append(EscapeBodyText(image.Target));
        sb.Append(")]\n");
    }

    private static void RenderTable(StringBuilder sb, TableNode table)
    {
        if (table.Title is not null)
        {
            sb.Append(".PP\n\\fB");
            sb.Append(EscapeBodyText(table.Title));
            sb.Append("\\fR\n");
        }

        sb.Append(".nf\n");
        foreach (var child in table.Children)
        {
            if (child is TableRowNode row)
            {
                bool first = true;
                foreach (var cell in row.Children)
                {
                    if (cell is TableCellNode cellNode)
                    {
                        if (!first) sb.Append('\t');
                        sb.Append(EscapeBodyText(cellNode.Text));
                        first = false;
                    }
                }
                sb.Append('\n');
            }
        }
        sb.Append(".fi\n");
    }

    private static void RenderStemBlock(StringBuilder sb, StemBlockNode stem)
    {
        if (stem.Title is not null)
        {
            sb.Append(".PP\n\\fB");
            sb.Append(EscapeBodyText(stem.Title));
            sb.Append("\\fR\n");
        }
        sb.Append(".nf\n");
        sb.Append(EscapeBodyText(stem.Content));
        sb.Append('\n');
        sb.Append(".fi\n");
    }

    private static void RenderBibliographyEntry(
        StringBuilder sb, BibliographyEntryNode entry)
    {
        sb.Append(".IP \"[");
        sb.Append(EscapeRoff(entry.RefId));
        sb.Append("]\" 6\n");
        if (entry.Inlines.Count > 0)
        {
            RenderInlines(sb, entry.Inlines);
            sb.Append('\n');
        }
        else
        {
            sb.Append(EscapeBodyText(entry.Text));
            sb.Append('\n');
        }
    }

    // Inline rendering, utilities, escaping -> ManRendererInlines.cs

    /// <summary>
    /// Escapes special characters for use inside roff quoted arguments.
    /// </summary>
    internal static string EscapeRoff(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\(dq");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Escapes text for use in roff body content (not inside quotes).
    /// Handles leading dots and apostrophes that would be interpreted as directives.
    /// </summary>
    internal static string EscapeBodyText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var sb = new StringBuilder(text.Length);
        bool lineStart = true;
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (lineStart && (c == '.' || c == '\''))
            {
                sb.Append("\\&");
                sb.Append(c);
                lineStart = false;
            }
            else if (c == '\\')
            {
                sb.Append("\\\\");
                lineStart = false;
            }
            else if (c == '\n')
            {
                sb.Append('\n');
                lineStart = true;
            }
            else
            {
                sb.Append(c);
                lineStart = false;
            }
        }
        return sb.ToString();
    }
}
