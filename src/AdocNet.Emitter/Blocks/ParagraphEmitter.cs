using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class ParagraphEmitter
{
    public static void Emit(ParagraphNode paragraph, EmitContext ctx)
    {
        BlockAttributesEmitter.Emit(paragraph, ctx);

        if (paragraph.Inlines.Count > 0)
            InlineEmitter.EmitAll(paragraph.Inlines, ctx);
        else
            ctx.Output.Append(paragraph.Text);

        if (ctx.Output.Length == 0 || ctx.Output[ctx.Output.Length - 1] != '\n')
            ctx.Output.Append('\n');
    }
}
