using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class ListEmitter
{
    public static void Emit(ListNode list, EmitContext ctx)
    {
        BlockAttributesEmitter.Emit(list, ctx);

        // Asciidoctor uses [arabic], [loweralpha], etc. and start=N to control
        // ordered-list numbering style and start value. Emit these as a single
        // block-attribute line when present.
        EmitListMetaAttributes(list, ctx);

        EmitItems(list, ctx, depth: 1);
    }

    private static void EmitItems(ListNode list, EmitContext ctx, int depth)
    {
        char marker = list.ListKind == ListKind.Unordered
            ? ctx.Options.UnorderedListMarker
            : '.';

        foreach (var child in list.Children)
        {
            if (child is not ListItemNode item) continue;

            // Marker line: marker × depth + space + item text.
            for (int i = 0; i < depth; i++)
                ctx.Output.Append(marker);
            ctx.Output.Append(' ');

            // Optional checklist prefix.
            if (item.Checked is bool checkedState)
                ctx.Output.Append(checkedState ? "[x] " : "[ ] ");

            // Prefer the raw Text over synthesised inlines for the same
            // reason as ParagraphEmitter — the parser keeps literal source
            // there (e.g. `--` vs the post-replacement em-dash) and using
            // it directly gives a byte-faithful round-trip.
            if (!string.IsNullOrEmpty(item.Text))
                ctx.Output.Append(item.Text);
            else if (item.Inlines.Count > 0)
                InlineEmitter.EmitAll(item.Inlines, ctx);
            ctx.Output.Append('\n');

            // Nested content. Children of a ListItemNode may be:
            //   - nested ListNode (deeper marker)
            //   - DescriptionListNode (nested description list)
            //   - paragraph / delimited block / etc. as a list continuation
            for (int i = 0; i < item.Children.Count; i++)
            {
                var nested = item.Children[i];
                if (nested is ListNode subList)
                {
                    // A nested list carries its own numbering style/start, so
                    // its attribute line goes immediately above its first item;
                    // without it a `[loweralpha]` sub-list would re-parse as a
                    // plain arabic one.
                    EmitListMetaAttributes(subList, ctx);
                    EmitItems(subList, ctx, depth + 1);
                }
                else
                {
                    // Continuation block: separator '+' on its own line, then the block.
                    ctx.Output.Append("+\n");
                    AsciidocEmitter.EmitNode(nested, ctx);
                }
            }
        }
    }

    private static void EmitListMetaAttributes(ListNode list, EmitContext ctx)
    {
        bool hasStart = list.Start is not null;
        bool hasStyle = !string.IsNullOrEmpty(list.ListStyle);
        if (!hasStart && !hasStyle) return;

        ctx.Output.Append('[');
        if (hasStyle)
            ctx.Output.Append(list.ListStyle);
        if (hasStart)
        {
            if (hasStyle) ctx.Output.Append(", ");
            ctx.Output.Append("start=");
            ctx.Output.Append(list.Start!.Value);
        }
        ctx.Output.Append("]\n");
    }

    public static void EmitDescription(DescriptionListNode list, EmitContext ctx)
    {
        BlockAttributesEmitter.Emit(list, ctx);

        if (!string.IsNullOrEmpty(list.Style))
        {
            ctx.Output.Append('[');
            ctx.Output.Append(list.Style);
            ctx.Output.Append("]\n");
        }

        foreach (var child in list.Children)
        {
            if (child is not DescriptionItemNode item) continue;
            EmitDescriptionItem(item, ctx);
        }
    }

    private static void EmitDescriptionItem(DescriptionItemNode item, EmitContext ctx)
    {
        // Each term gets its own "term::" line (single-term items are the
        // common case; multi-term items emit one line per term followed by a
        // single description line, matching the parser's input shape).
        var allTermInlines = item.AllTermInlines;
        for (int t = 0; t < item.Terms.Count; t++)
        {
            if (allTermInlines is not null && t < allTermInlines.Count && allTermInlines[t].Count > 0)
                InlineEmitter.EmitAll(allTermInlines[t], ctx);
            else if (t == 0 && item.TermInlines.Count > 0)
                InlineEmitter.EmitAll(item.TermInlines, ctx);
            else
                ctx.Output.Append(item.Terms[t]);
            ctx.Output.Append("::");
            if (t < item.Terms.Count - 1)
                ctx.Output.Append('\n');
        }

        // Description goes on the next line; empty descriptions still get the
        // line terminator so the term:: marker is well-formed.
        ctx.Output.Append('\n');
        // Same Text-vs-Inlines preference as ListItemNode/ParagraphNode.
        bool hasDescription = !string.IsNullOrEmpty(item.Description);
        if (hasDescription)
            ctx.Output.Append(item.Description);
        else if (item.DescriptionInlines.Count > 0)
        {
            InlineEmitter.EmitAll(item.DescriptionInlines, ctx);
            hasDescription = true;
        }

        // If there's no inline description and a nested block follows, omit
        // the blank line — Asciidoctor requires the `+` continuation marker
        // to be on the line immediately after the `Term::` line. A blank
        // line between them breaks the dlist scope and the `+` becomes a
        // paragraph of its own.
        if (hasDescription || item.Children.Count == 0)
            ctx.Output.Append('\n');

        // Nested blocks attached to this dlist item.
        //
        // AsciiDoc supports two attachment styles:
        //   1. **Indented** for nested lists (ulist/olist/dlist). Lines under
        //      a dlist term may be indented by whitespace; the parser then
        //      reads them as children of the dlist item.
        //   2. **Continuation (`+`)** for other blocks (paragraphs, source,
        //      example, sidebar, etc.). The `+` on its own line attaches the
        //      next block to the preceding list item.
        //
        // We pick the style per child: nested lists use indentation,
        // everything else uses `+`.
        foreach (var child in item.Children)
        {
            if (child is ListNode or DescriptionListNode)
            {
                EmitIndentedChild(child, ctx, indent: "  ");
            }
            else
            {
                ctx.Output.Append("+\n");
                AsciidocEmitter.EmitNode(child, ctx);
            }
        }
    }

    /// <summary>
    /// Emits <paramref name="child"/> as a nested-under-dlist block by
    /// prefixing every emitted line with <paramref name="indent"/>. This is
    /// how AsciiDoc represents lists nested inside a description list item.
    /// </summary>
    private static void EmitIndentedChild(AstNode child, EmitContext ctx, string indent)
    {
        int startMark = ctx.Output.Length;
        AsciidocEmitter.EmitNode(child, ctx);
        if (ctx.Output.Length == startMark) return;

        // Pull out what was just emitted, indent it line-by-line, write back.
        var emitted = ctx.Output.ToString(startMark, ctx.Output.Length - startMark);
        ctx.Output.Length = startMark;
        var lines = emitted.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            // Empty trailing line after a final '\n' — skip indent so we
            // don't dump a lone "  " onto the output.
            if (i == lines.Length - 1 && lines[i].Length == 0)
            {
                ctx.Output.Append('\n');
                break;
            }
            if (lines[i].Length > 0)
                ctx.Output.Append(indent);
            ctx.Output.Append(lines[i]);
            if (i < lines.Length - 1)
                ctx.Output.Append('\n');
        }
    }
}
