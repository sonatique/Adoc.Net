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

        // Inline admonition form: TYPE: text-on-this-line
        bool isInline = admon.Children.Count == 0 && (admon.Inlines.Count > 0 || admon.Text is not null);
        if (isInline)
        {
            ctx.Output.Append(admon.AdmonitionType);
            ctx.Output.Append(": ");
            if (admon.Inlines.Count > 0)
                InlineEmitter.EmitAll(admon.Inlines, ctx);
            else if (admon.Text is not null)
                ctx.Output.Append(admon.Text);
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
