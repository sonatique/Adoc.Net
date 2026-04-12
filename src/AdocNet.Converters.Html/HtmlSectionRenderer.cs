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
}
