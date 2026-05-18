using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class AdmonitionEmitter
{
    public static void Emit(AdmonitionNode admon, EmitContext ctx)
    {
        if (!string.IsNullOrEmpty(admon.Title))
        {
            ctx.Output.Append('.');
            ctx.Output.Append(admon.Title);
            ctx.Output.Append('\n');
        }

        BlockAttributesEmitter.Emit(admon, ctx);

        // Inline admonition form: TYPE: text-on-this-line. Prefer raw Text
        // over synthesised inlines for the same reason as ParagraphEmitter —
        // Text carries the literal source (e.g. an auto-detected mailto kept
        // as `sales@example.com` rather than expanded to `link:mailto:…[]`).
        bool isInline = admon.Children.Count == 0 && (admon.Inlines.Count > 0 || admon.Text is not null);
        if (isInline)
        {
            ctx.Output.Append(admon.AdmonitionType);
            ctx.Output.Append(": ");
            if (!string.IsNullOrEmpty(admon.Text))
                ctx.Output.Append(admon.Text);
            else if (admon.Inlines.Count > 0)
                InlineEmitter.EmitAll(admon.Inlines, ctx);
            ctx.Output.Append('\n');
            return;
        }

        // Block admonition form: [TYPE] header, then a ==== example block.
        ctx.Output.Append('[');
        ctx.Output.Append(admon.AdmonitionType);
        ctx.Output.Append("]\n");
        ctx.Output.Append("====\n");
        DocumentEmitter.EmitBlocks(admon.Children, ctx);
        if (ctx.Output.Length > 0 && ctx.Output[ctx.Output.Length - 1] != '\n')
            ctx.Output.Append('\n');
        ctx.Output.Append("====\n");
    }
}
