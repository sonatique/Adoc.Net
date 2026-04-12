using System.Text;
using System.Text.RegularExpressions;
using AdocNet;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Converters.Html;

public sealed partial class HtmlRenderer
{
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
}
