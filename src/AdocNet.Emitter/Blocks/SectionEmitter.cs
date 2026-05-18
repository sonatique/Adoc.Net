using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class SectionEmitter
{
    public static void Emit(SectionNode section, EmitContext ctx)
    {
        BlockAttributesEmitter.Emit(section, ctx);

        // Style attribute line on the line above the heading. Examples:
        // [discrete], [appendix], [glossary], [colophon], [dedication],
        // [preface]. IsDiscrete is a separate boolean from the Style string
        // on the AST, so handle both.
        if (section.IsDiscrete)
            ctx.Output.Append("[discrete]\n");
        else if (!string.IsNullOrEmpty(section.Style))
        {
            ctx.Output.Append('[');
            ctx.Output.Append(section.Style);
            ctx.Output.Append("]\n");
        }

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
