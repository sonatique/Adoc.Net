using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class ParagraphEmitter
{
    public static void Emit(ParagraphNode paragraph, EmitContext ctx)
    {
        BlockAttributesEmitter.Emit(paragraph, ctx);

        // [%hardbreaks] / [verse] / [abstract] style attribute line.
        if (!string.IsNullOrEmpty(paragraph.Style))
        {
            ctx.Output.Append('[');
            ctx.Output.Append(paragraph.Style);
            ctx.Output.Append("]\n");
        }
        else if (paragraph.HasHardbreaks)
        {
            ctx.Output.Append("[%hardbreaks]\n");
        }

        // Prefer the raw Text when populated — it carries the literal source
        // (e.g. <c>--</c> rather than the post-replacement em-dash) and gives
        // a faithful round-trip. Fall back to synthesised inlines only when
        // the AST was constructed without raw text (synthetic mutations).
        if (!string.IsNullOrEmpty(paragraph.Text))
            ctx.Output.Append(paragraph.Text);
        else if (paragraph.Inlines.Count > 0)
            InlineEmitter.EmitAll(paragraph.Inlines, ctx);

        if (ctx.Output.Length == 0 || ctx.Output[ctx.Output.Length - 1] != '\n')
            ctx.Output.Append('\n');
    }
}
