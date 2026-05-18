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

            if (item.Inlines.Count > 0)
                InlineEmitter.EmitAll(item.Inlines, ctx);
            else
                ctx.Output.Append(item.Text);
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

        // Description on the next line; empty descriptions still get the line
        // terminator so the term:: marker is well-formed.
        ctx.Output.Append('\n');
        if (item.DescriptionInlines.Count > 0)
            InlineEmitter.EmitAll(item.DescriptionInlines, ctx);
        else if (!string.IsNullOrEmpty(item.Description))
            ctx.Output.Append(item.Description);
        ctx.Output.Append('\n');

        // Any nested blocks attached to this dlist item.
        foreach (var nested in item.Children)
        {
            ctx.Output.Append("+\n");
            AsciidocEmitter.EmitNode(nested, ctx);
        }
    }
}
