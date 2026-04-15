using System.Text;
using System.Text.RegularExpressions;
using AdocNet;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Converters.Html;

public sealed partial class HtmlRenderer
{
    private void RenderSection(StringBuilder sb, SectionNode section, bool useIconFont, FootnoteState footnotes, SectionNumberingContext secCtx, HtmlRenderState state)
    {
        // Level 0 = = (book parts) -> <h1>, Level 1 = == -> <h2>, etc.
        var tag = section.Level switch
        {
            0 => "h1",
            1 => "h2",
            2 => "h3",
            3 => "h4",
            4 => "h5",
            _ => "h6",
        };

        var sectionNumberingEnabled = section.SectnumsEnabled ?? secCtx.Enabled;
        var prefix = section.IsDiscrete || !sectionNumberingEnabled ? null : secCtx.Advance(section.Level);

        // Discrete headings: bare heading element, no wrapper div.
        // class="discrete" marks the heading as a floating title.
        if (section.IsDiscrete)
        {
            sb.Append('<');
            sb.Append(tag);
            if (section.Id is not null)
            {
                sb.Append(" id=\"");
                EscapeTo(sb, section.Id);
                sb.Append('"');
            }
            sb.Append(" class=\"discrete\"");
            sb.Append('>');
            RenderInlines(sb, section.TitleInlines, section.Title, footnotes, state);
            sb.Append("</");
            sb.Append(tag);
            sb.Append(">\n");
            RenderChildBlocks(sb, section.Children, useIconFont, footnotes, secCtx, state);
            return;
        }

        // Non-discrete sections: <div class="sectN [roles]"> wrapper.
        // Roles go on the wrapper div (matching Asciidoctor behavior).
        sb.Append("<div class=\"sect");
        sb.Append(section.Level);
        for (int i = 0; i < section.Roles.Count; i++)
        {
            sb.Append(' ');
            EscapeTo(sb, section.Roles[i]);
        }
        sb.Append("\">\n");

        // Heading tag with id
        sb.Append('<');
        sb.Append(tag);
        if (section.Id is not null)
        {
            sb.Append(" id=\"");
            EscapeTo(sb, section.Id);
            sb.Append('"');
        }
        sb.Append('>');

        // :sectanchors: — anchor icon before heading content
        if (section.Id is not null && state.DocumentAttributes.ContainsKey("sectanchors"))
        {
            sb.Append("<a class=\"anchor\" href=\"#");
            EscapeTo(sb, section.Id);
            sb.Append("\"></a>");
        }
        // :sectlinks: — wrap heading content in self-link
        var sectlinkId = section.Id is not null && state.DocumentAttributes.ContainsKey("sectlinks")
            ? section.Id : null;
        if (sectlinkId is not null)
        {
            sb.Append("<a class=\"link\" href=\"#");
            EscapeTo(sb, sectlinkId);
            sb.Append("\">");
        }
        // Book part prefix: "Part I. ", "Part II. ", etc. (:doctype: book + level 0)
        if (section.Level == 0
            && state.DocumentAttributes.TryGetValue("doctype", out var doctype)
            && string.Equals(doctype, "book", StringComparison.OrdinalIgnoreCase))
        {
            state.PartCounter++;
            sb.Append("Part ");
            sb.Append(ToRoman(state.PartCounter));
            sb.Append(". ");
        }
        // Appendix prefix: "Appendix A: ", "Appendix B: ", etc.
        else if (string.Equals(section.Style, "appendix", StringComparison.OrdinalIgnoreCase))
        {
            char letter = (char)('A' + state.AppendixCounter++);
            sb.Append("Appendix ");
            sb.Append(letter);
            sb.Append(": ");
        }
        else if (prefix is not null)
        {
            sb.Append(prefix);
        }
        RenderInlines(sb, section.TitleInlines, section.Title, footnotes, state);
        if (sectlinkId is not null)
            sb.Append("</a>");
        sb.Append("</");
        sb.Append(tag);
        sb.Append(">\n");

        // Level-1 sections (sect1) wrap children in <div class="sectionbody">.
        // Level 2+ sections render children directly inside the sectN div.
        if (section.Level == 1)
        {
            sb.Append("<div class=\"sectionbody\">\n");
            RenderChildBlocks(sb, section.Children, useIconFont, footnotes, secCtx, state);
            sb.Append("</div>\n");
        }
        else
        {
            RenderChildBlocks(sb, section.Children, useIconFont, footnotes, secCtx, state);
        }

        sb.Append("</div>\n");
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

    private static readonly int[] RomanValues = [1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1];
    private static readonly string[] RomanNumerals = ["M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"];

    private static string ToRoman(int number)
    {
        if (number <= 0) return number.ToString();
        var sb = new StringBuilder();
        for (int j = 0; j < RomanValues.Length; j++)
        {
            while (number >= RomanValues[j])
            {
                sb.Append(RomanNumerals[j]);
                number -= RomanValues[j];
            }
        }
        return sb.ToString();
    }
}
