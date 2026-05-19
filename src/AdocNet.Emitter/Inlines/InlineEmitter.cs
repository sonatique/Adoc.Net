using AdocNet.Ast;

namespace AdocNet.Emitter;

/// <summary>
/// Dispatch for inline nodes. Each inline node is either:
/// <list type="bullet">
///   <item><description>copied verbatim via the source-anchored fast path
///     when its <see cref="AstNode.Source"/> is populated and an original
///     source is available; or</description></item>
///   <item><description>synthesised from its typed AST properties using the
///     per-type emitter functions in this file.</description></item>
/// </list>
/// </summary>
internal static class InlineEmitter
{
    public static void EmitAll(IReadOnlyList<InlineNode> nodes, EmitContext ctx)
    {
        foreach (var node in nodes)
            Emit(node, ctx);
    }

    public static void Emit(InlineNode node, EmitContext ctx)
    {
        if (ctx.TryEmitOriginal(node)) return;

        switch (node)
        {
            case TextInlineNode text:
                ctx.Output.Append(text.Value);
                break;

            case StrongInlineNode strong:
                EmitInlineRolePrefix(strong.Roles, ctx);
                ctx.Output.Append('*');
                EmitAll(strong.Children, ctx);
                ctx.Output.Append('*');
                break;

            case EmphasisInlineNode em:
                EmitInlineRolePrefix(em.Roles, ctx);
                ctx.Output.Append('_');
                EmitAll(em.Children, ctx);
                ctx.Output.Append('_');
                break;

            case MonospaceInlineNode mono:
                EmitInlineRolePrefix(mono.Roles, ctx);
                ctx.Output.Append('`');
                EmitAll(mono.Children, ctx);
                ctx.Output.Append('`');
                break;

            case HighlightInlineNode hl:
                EmitInlineIdRolePrefix(hl.Id, hl.Roles, ctx);
                ctx.Output.Append('#');
                EmitAll(hl.Children, ctx);
                ctx.Output.Append('#');
                break;

            case SubscriptInlineNode sub:
                ctx.Output.Append('~');
                ctx.Output.Append(sub.Content);
                ctx.Output.Append('~');
                break;

            case SuperscriptInlineNode sup:
                ctx.Output.Append('^');
                ctx.Output.Append(sup.Content);
                ctx.Output.Append('^');
                break;

            case PassthroughInlineNode pass:
                EmitPassthrough(pass, ctx);
                break;

            case LinkInlineNode bareLink:
                ctx.Output.Append(bareLink.Url);
                break;

            case InlineLinkMacroNode link:
                ctx.Output.Append("link:");
                ctx.Output.Append(link.Url);
                ctx.Output.Append('[');
                EmitLinkMacroAttributes(link, ctx);
                ctx.Output.Append(']');
                break;

            case InlineImageNode image:
                ctx.Output.Append("image:");
                ctx.Output.Append(image.Target);
                ctx.Output.Append('[');
                EmitInlineImageAttributes(image, ctx);
                ctx.Output.Append(']');
                break;

            case InlineMacroNode macro:
                ctx.Output.Append(macro.Name);
                ctx.Output.Append(':');
                ctx.Output.Append(macro.Target);
                ctx.Output.Append('[');
                ctx.Output.Append(macro.Content);
                ctx.Output.Append(']');
                break;

            case CrossReferenceInlineNode xref:
                ctx.Output.Append("<<");
                ctx.Output.Append(xref.Target);
                if (xref.Label is not null)
                {
                    ctx.Output.Append(',');
                    ctx.Output.Append(xref.Label);
                }
                ctx.Output.Append(">>");
                break;

            case InterDocumentXrefNode interXref:
                ctx.Output.Append("xref:");
                ctx.Output.Append(interXref.Path);
                if (interXref.Id is not null)
                {
                    ctx.Output.Append('#');
                    ctx.Output.Append(interXref.Id);
                }
                ctx.Output.Append('[');
                if (interXref.Label is not null)
                    ctx.Output.Append(interXref.Label);
                ctx.Output.Append(']');
                break;

            case FootnoteInlineNode footnote:
                ctx.Output.Append("footnote:");
                if (footnote.Id is not null)
                    ctx.Output.Append(footnote.Id);
                ctx.Output.Append('[');
                if (footnote.Inlines.Count > 0)
                    EmitAll(footnote.Inlines, ctx);
                else if (footnote.Text is not null)
                    ctx.Output.Append(footnote.Text);
                ctx.Output.Append(']');
                break;

            case InlineAnchorNode anchor:
                ctx.Output.Append("[[");
                ctx.Output.Append(anchor.Id);
                if (anchor.Reftext is not null)
                {
                    ctx.Output.Append(',');
                    ctx.Output.Append(anchor.Reftext);
                }
                ctx.Output.Append("]]");
                break;

            case StemInlineNode stem:
                ctx.Output.Append(stem.StemType);
                ctx.Output.Append(":[");
                ctx.Output.Append(stem.Content);
                ctx.Output.Append(']');
                break;

            default:
                // Unknown inline type: emit a sentinel that round-trip tests
                // will surface as a structural diff.
                ctx.Output.Append("<!emitter:unhandled:");
                ctx.Output.Append(node.Kind);
                ctx.Output.Append('>');
                break;
        }
    }

    private static void EmitInlineRolePrefix(IReadOnlyList<string>? roles, EmitContext ctx)
    {
        if (roles is null || roles.Count == 0) return;
        ctx.Output.Append('[');
        for (int i = 0; i < roles.Count; i++)
        {
            ctx.Output.Append('.');
            ctx.Output.Append(roles[i]);
        }
        ctx.Output.Append(']');
    }

    private static void EmitInlineIdRolePrefix(string? id, IReadOnlyList<string>? roles, EmitContext ctx)
    {
        bool hasId = !string.IsNullOrEmpty(id);
        bool hasRoles = roles is not null && roles.Count > 0;
        if (!hasId && !hasRoles) return;
        ctx.Output.Append('[');
        if (hasId)
        {
            ctx.Output.Append('#');
            ctx.Output.Append(id);
        }
        if (hasRoles)
        {
            foreach (var role in roles!)
            {
                ctx.Output.Append('.');
                ctx.Output.Append(role);
            }
        }
        ctx.Output.Append(']');
    }

    private static void EmitPassthrough(PassthroughInlineNode pass, EmitContext ctx)
    {
        // No substitutions → triple-plus preserves the content as-is regardless
        // of what's inside. With substitutions, pass:<subs>[content] is needed
        // to round-trip the substitution flags.
        if (pass.Substitutions == SubstitutionKind.None)
        {
            ctx.Output.Append("+++");
            ctx.Output.Append(pass.Content);
            ctx.Output.Append("+++");
        }
        else
        {
            ctx.Output.Append("pass:");
            ctx.Output.Append(SubstitutionKindToCsv(pass.Substitutions));
            ctx.Output.Append('[');
            ctx.Output.Append(pass.Content);
            ctx.Output.Append(']');
        }
    }

    private static void EmitLinkMacroAttributes(InlineLinkMacroNode link, EmitContext ctx)
    {
        // Positional first argument is the label.
        ctx.Output.Append(link.Label);

        var hasWindow = !string.IsNullOrEmpty(link.Window);
        var hasRole = !string.IsNullOrEmpty(link.Role);
        if (hasWindow)
        {
            ctx.Output.Append(", window=");
            ctx.Output.Append(link.Window);
        }
        if (hasRole)
        {
            ctx.Output.Append(", role=");
            ctx.Output.Append(link.Role);
        }
    }

    private static void EmitInlineImageAttributes(InlineImageNode image, EmitContext ctx)
    {
        // Positional: alt, width, height.
        ctx.Output.Append(image.Alt);
        bool hasWidth = !string.IsNullOrEmpty(image.Width);
        bool hasHeight = !string.IsNullOrEmpty(image.Height);
        if (hasWidth || hasHeight)
        {
            ctx.Output.Append(',');
            ctx.Output.Append(image.Width ?? string.Empty);
        }
        if (hasHeight)
        {
            ctx.Output.Append(',');
            ctx.Output.Append(image.Height);
        }
    }

    private static string SubstitutionKindToCsv(SubstitutionKind kind)
    {
        // Asciidoctor accepts symbolic names for the substitution categories.
        // The mapping mirrors AdocNet.Parser/InlineParser.ParseSubstitutionNames
        // so that emitting then re-parsing yields the same flags.
        var parts = new List<string>();
        if (kind.HasFlag(SubstitutionKind.SpecialCharacters)) parts.Add("specialcharacters");
        if (kind.HasFlag(SubstitutionKind.Quotes)) parts.Add("quotes");
        if (kind.HasFlag(SubstitutionKind.Attributes)) parts.Add("attributes");
        if (kind.HasFlag(SubstitutionKind.Replacements)) parts.Add("replacements");
        if (kind.HasFlag(SubstitutionKind.Macros)) parts.Add("macros");
        if (kind.HasFlag(SubstitutionKind.PostReplacements)) parts.Add("post_replacements");
        return parts.Count == 0 ? "none" : string.Join(",", parts);
    }
}
