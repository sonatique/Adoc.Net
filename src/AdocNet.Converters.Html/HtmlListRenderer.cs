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

        // Compute effective style before emitting any HTML — needed for both the outer
        // wrapper div class and the inner <ol> class.
        // Asciidoctor emits a list style class (e.g. "arabic" for default ordered lists).
        // When no explicit style is set, auto-assign by nesting depth:
        //   depth 0 → arabic, 1 → loweralpha, 2 → lowerroman, 3+ → cycle
        string? effectiveStyle = null;
        if (list.ListKind == ListKind.Ordered)
        {
            effectiveStyle = list.ListStyle ?? orderedListDepth switch
            {
                0 => "arabic",
                1 => "loweralpha",
                2 => "lowerroman",
                _ => "arabic",
            };
            nextDepth = orderedListDepth + 1;
        }

        // Outer wrapper div: Asciidoctor always emits <div class="ulist"> or <div class="olist arabic">.
        // IDs and roles go on this outer div, not on the inner <ul>/<ol>.
        sb.Append("<div class=\"");
        if (list.ListKind == ListKind.Unordered)
        {
            sb.Append(isChecklist ? "ulist checklist" : "ulist");
        }
        else
        {
            sb.Append("olist ");
            sb.Append(effectiveStyle);
        }
        for (int i = 0; i < list.Roles.Count; i++)
        {
            sb.Append(' ');
            EscapeTo(sb, list.Roles[i]);
        }
        sb.Append('"');
        if (list.Id is not null)
        {
            sb.Append(" id=\"");
            EscapeTo(sb, list.Id);
            sb.Append('"');
        }
        sb.Append(">\n");

        // Inner <ul> or <ol>
        sb.Append('<');
        sb.Append(tag);

        if (isChecklist)
            sb.Append(" class=\"checklist\"");

        if (list.ListKind == ListKind.Ordered)
        {
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
        sb.Append("</div>\n");
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
        bool firstChild = true;
        foreach (var child in item.Children)
        {
            // First child needs \n to separate from </p>; subsequent children
            // follow the previous child's trailing \n, so no extra separator needed.
            if (firstChild)
            {
                sb.Append('\n');
                firstChild = false;
            }

            if (child is ListNode nestedList)
                RenderList(sb, nestedList, footnotes, state, orderedListDepth);
            else if (child is DelimitedBlockNode block)
                RenderDelimitedBlock(sb, block, footnotes, new SectionNumberingContext(), state);
            else if (child is AdmonitionNode admonition)
                RenderAdmonition(sb, admonition, false, footnotes, new SectionNumberingContext(), state);
            else if (child is ParagraphNode para)
                RenderParagraph(sb, para, footnotes, state);
        }

        // Nested blocks already emit a trailing \n, so only add one if no children rendered
        if (item.Children.Count == 0)
            sb.Append('\n');
        sb.Append("</li>\n");
    }

    private void RenderDescriptionList(StringBuilder sb, DescriptionListNode list, bool useIconFont, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        if (list.Style == "qanda")
        {
            RenderQandaList(sb, list, footnotes, state);
            return;
        }
        if (list.Style == "horizontal")
        {
            RenderHorizontalList(sb, list, footnotes, state);
            return;
        }

        // Outer wrapper div: Asciidoctor always wraps <dl> in <div class="dlist">.
        sb.Append("<div class=\"dlist\"");
        if (list.Id is not null)
        {
            sb.Append(" id=\"");
            EscapeTo(sb, list.Id);
            sb.Append('"');
        }
        sb.Append(">\n");
        sb.Append("<dl>\n");
        foreach (var child in list.Children)
        {
            if (child is DescriptionItemNode item)
            {
                // Render all terms as separate <dt> elements
                if (item.AllTermInlines is { Count: > 0 })
                {
                    for (int t = 0; t < item.AllTermInlines.Count; t++)
                    {
                        sb.Append("<dt class=\"hdlist1\">");
                        RenderInlines(sb, item.AllTermInlines[t], item.Terms[t], footnotes, state);
                        sb.Append("</dt>\n");
                    }
                }
                else
                {
                    sb.Append("<dt class=\"hdlist1\">");
                    RenderInlines(sb, item.TermInlines, item.Terms[0], footnotes, state);
                    sb.Append("</dt>\n");
                }
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
        sb.Append("</div>\n");
    }

    private void RenderQandaList(StringBuilder sb, DescriptionListNode list, FootnoteState footnotes, HtmlRenderState state)
    {
        sb.Append("<div class=\"qlist qanda\">\n<ol>\n");
        foreach (var child in list.Children)
        {
            if (child is DescriptionItemNode item)
            {
                sb.Append("<li>\n<p><em>");
                RenderInlines(sb, item.TermInlines, item.Terms[0], footnotes, state);
                sb.Append("</em></p>\n");
                if (!string.IsNullOrEmpty(item.Description))
                {
                    sb.Append("<p>");
                    RenderInlines(sb, item.DescriptionInlines, item.Description, footnotes, state);
                    sb.Append("</p>\n");
                }
                sb.Append("</li>\n");
            }
        }
        sb.Append("</ol>\n</div>\n");
    }

    private void RenderHorizontalList(StringBuilder sb, DescriptionListNode list, FootnoteState footnotes, HtmlRenderState state)
    {
        sb.Append("<div class=\"hdlist\">\n<table>\n");
        foreach (var child in list.Children)
        {
            if (child is DescriptionItemNode item)
            {
                sb.Append("<tr>\n<td class=\"hdlist1\">\n");
                RenderInlines(sb, item.TermInlines, item.Terms[0], footnotes, state);
                sb.Append("\n</td>\n<td class=\"hdlist2\">\n");
                if (!string.IsNullOrEmpty(item.Description))
                {
                    sb.Append("<p>");
                    RenderInlines(sb, item.DescriptionInlines, item.Description, footnotes, state);
                    sb.Append("</p>");
                }
                sb.Append("\n</td>\n</tr>\n");
            }
        }
        sb.Append("</table>\n</div>\n");
    }
}
