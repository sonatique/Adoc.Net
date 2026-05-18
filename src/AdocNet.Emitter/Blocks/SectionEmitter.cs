using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class SectionEmitter
{
    public static void Emit(SectionNode section, EmitContext ctx)
    {
        BlockAttributesEmitter.Emit(section, ctx);

        // Discrete headings get a [discrete] style attribute on the line above.
        if (section.IsDiscrete)
            ctx.Output.Append("[discrete]\n");

        // Heading marker: '=' repeated Level+1 times (level 1 == '==', etc.).
        // SectionNode.Level uses 1 for top sections per Asciidoctor convention,
        // so the marker count is Level + 1.
        int markerCount = section.Level + 1;
        for (int i = 0; i < markerCount; i++)
            ctx.Output.Append('=');
        ctx.Output.Append(' ');
        ctx.Output.Append(section.Title);
        ctx.Output.Append('\n');

        if (section.Children.Count > 0)
        {
            ctx.Output.Append('\n');
            DocumentEmitter.EmitBlocks(section.Children, ctx);
        }
    }
}
