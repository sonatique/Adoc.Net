using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class PageBreakEmitter
{
    public static void Emit(PageBreakNode node, EmitContext ctx)
    {
        BlockAttributesEmitter.Emit(node, ctx);
        ctx.Output.Append("<<<\n");
    }
}
