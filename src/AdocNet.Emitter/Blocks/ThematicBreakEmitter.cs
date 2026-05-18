using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class ThematicBreakEmitter
{
    public static void Emit(ThematicBreakNode node, EmitContext ctx)
    {
        BlockAttributesEmitter.Emit(node, ctx);
        ctx.Output.Append("'''\n");
    }
}
