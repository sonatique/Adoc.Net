using System.Text;
using System.Text.RegularExpressions;
using AdocNet;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Converters.Html;

public sealed partial class HtmlRenderer
{
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
        if (TryRenderTemplate(sb, node))
            return;

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
                    sb.Append('"');
                    if (highlight.Id is not null)
                    {
                        sb.Append(" id=\"");
                        EscapeTo(sb, highlight.Id);
                        sb.Append('"');
                    }
                    sb.Append('>');
                    foreach (var child in highlight.Children)
                        RenderInline(sb, child, footnotes, state);
                    sb.Append("</span>");
                }
                else
                {
                    if (highlight.Id is not null)
                    {
                        sb.Append("<mark id=\"");
                        EscapeTo(sb, highlight.Id);
                        sb.Append("\">");
                    }
                    else
                    {
                        sb.Append("<mark>");
                    }
                    foreach (var child in highlight.Children)
                        RenderInline(sb, child, footnotes, state);
                    sb.Append("</mark>");
                }
                break;

            case LinkInlineNode link:
                sb.Append("<a href=\"");
                EscapeTo(sb, link.Url);
                sb.Append("\" class=\"bare\">");
                var displayUrl = state.DocumentAttributes.ContainsKey("hide-uri-scheme")
                    ? StripUriScheme(link.Url)
                    : link.Url;
                EscapeTo(sb, displayUrl);
                sb.Append("</a>");
                break;

            case InlineLinkMacroNode linkMacro:
            {
                // Asciidoctor adds class="bare" when the link macro has no explicit
                // label (the URL itself becomes the display text).
                bool isBare = string.IsNullOrEmpty(linkMacro.Label) ||
                              linkMacro.Label == linkMacro.Url;
                sb.Append("<a href=\"");
                EscapeTo(sb, linkMacro.Url);
                sb.Append('"');
                if (linkMacro.Role is not null)
                {
                    sb.Append(" class=\"");
                    EscapeTo(sb, linkMacro.Role);
                    sb.Append('"');
                }
                else if (isBare)
                {
                    sb.Append(" class=\"bare\"");
                }
                if (linkMacro.Window is not null)
                {
                    sb.Append(" target=\"");
                    EscapeTo(sb, linkMacro.Window);
                    sb.Append('"');
                    if (linkMacro.Window == "_blank")
                        sb.Append(" rel=\"noopener\"");
                }
                sb.Append('>');
                if (isBare)
                {
                    EscapeTo(sb, linkMacro.Url);
                }
                else
                {
                    // Parse formatting + typographic substitutions (smart quotes,
                    // replacements). Skip Macros to avoid recursion — macros inside
                    // labels would re-enter link parsing and cause infinite recursion.
                    // Asciidoctor parity: link labels get the full text-substitution
                    // pipeline minus Macros, so contractions like "table's" render
                    // as "table’s" with curly apostrophe.
                    var labelSubs = SubstitutionKind.Quotes |
                                    SubstitutionKind.Replacements |
                                    SubstitutionKind.PostReplacements;
                    if (state.DocumentAttributes.ContainsKey("smartquotes")
                        && state.DocumentAttributes.TryGetValue("smartquotes", out var sq) && sq.StartsWith("!"))
                        labelSubs &= ~SubstitutionKind.PostReplacements;
                    var labelInlines = InlineParser.Parse(linkMacro.Label!, labelSubs, state.DocumentAttributes);
                    RenderInlines(sb, labelInlines, linkMacro.Label!, footnotes, state);
                }
                sb.Append("</a>");
                break;
            }

            case InlineImageNode inlineImage:
                sb.Append("<span class=\"image\"><img src=\"");
                AppendImageSrc(sb, inlineImage.Target, state);
                sb.Append("\" alt=\"");
                EscapeTo(sb, inlineImage.Alt);
                sb.Append('"');
                if (inlineImage.Width is not null)
                {
                    sb.Append(" width=\"");
                    EscapeTo(sb, inlineImage.Width);
                    sb.Append('"');
                }
                if (inlineImage.Height is not null)
                {
                    sb.Append(" height=\"");
                    EscapeTo(sb, inlineImage.Height);
                    sb.Append('"');
                }
                sb.Append("></span>");
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
                {
                    var xrefLabelInlines = InlineParser.Parse(xref.Label, SubstitutionKind.Quotes, state.DocumentAttributes);
                    RenderInlines(sb, xrefLabelInlines, xref.Label, footnotes, state);
                }
                else
                {
                    var xrefTitle = resolvedTitle ?? (state.IdTitles.TryGetValue(resolvedId, out var rt) ? rt : null);
                    var xrefStyle = state.DocumentAttributes.TryGetValue("xrefstyle", out var xs) ? xs : null;
                    if (xrefTitle is not null && xrefStyle is not null
                        && state.IdNumbers.TryGetValue(resolvedId, out var secNum))
                    {
                        if (xrefStyle == "short")
                        {
                            sb.Append("Section ");
                            EscapeTo(sb, secNum);
                        }
                        else if (xrefStyle == "full")
                        {
                            sb.Append("Section ");
                            EscapeTo(sb, secNum);
                            sb.Append(", &#8220;");
                            EscapeTo(sb, xrefTitle);
                            sb.Append("&#8221;");
                        }
                        else
                        {
                            EscapeTo(sb, xrefTitle);
                        }
                    }
                    else if (xrefTitle is not null)
                    {
                        EscapeTo(sb, xrefTitle);
                    }
                    else
                    {
                        sb.Append('[');
                        EscapeTo(sb, xref.Target);
                        sb.Append(']');
                    }
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
                    {
                        // Apply typographic substitutions to the label so contractions
                        // ("table's") render with curly apostrophe (asciidoctor parity).
                        var ixSubs = SubstitutionKind.Quotes |
                                     SubstitutionKind.Replacements |
                                     SubstitutionKind.PostReplacements;
                        if (state.DocumentAttributes.TryGetValue("smartquotes", out var sq2) && sq2.StartsWith("!"))
                            ixSubs &= ~SubstitutionKind.PostReplacements;
                        var ixLabelInlines = InlineParser.Parse(interXref.Label, ixSubs, state.DocumentAttributes);
                        RenderInlines(sb, ixLabelInlines, interXref.Label, footnotes, state);
                    }
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
                sb.Append(">[<a");
                if (!isBackRef)
                {
                    sb.Append(" id=\"_footnoteref_");
                    sb.Append(num);
                    sb.Append('"');
                }
                sb.Append(" class=\"footnote\" href=\"#_footnotedef_");
                sb.Append(num);
                sb.Append("\" title=\"View footnote.\">");
                sb.Append(num);
                sb.Append("</a>]</sup>");
                break;
            }

            case InlineAnchorNode anchor:
                sb.Append("<a id=\"");
                EscapeTo(sb, anchor.Id);
                sb.Append("\"></a>");
                break;

            case StemInlineNode stemInline:
                if (string.Equals(stemInline.StemType, "asciimath", StringComparison.OrdinalIgnoreCase))
                {
                    sb.Append("\\$");
                    sb.Append(stemInline.Content);
                    sb.Append("\\$");
                }
                else
                {
                    sb.Append("\\(");
                    sb.Append(stemInline.Content);
                    sb.Append("\\)");
                }
                break;

            case InlineMacroNode macro:
                RenderInlineMacro(sb, macro, state);
                break;

            case IndexTermNode indexTerm:
                // Visible index term: render all terms as comma-separated text
                for (int t = 0; t < indexTerm.Terms.Count; t++)
                {
                    if (t > 0) sb.Append(", ");
                    EscapeTo(sb, indexTerm.Terms[t]);
                }
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
}
