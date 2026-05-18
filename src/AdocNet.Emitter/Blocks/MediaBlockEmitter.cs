using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class MediaBlockEmitter
{
    public static void EmitImage(BlockImageNode node, EmitContext ctx)
    {
        if (!string.IsNullOrEmpty(node.Title))
        {
            ctx.Output.Append('.');
            ctx.Output.Append(node.Title);
            ctx.Output.Append('\n');
        }
        BlockAttributesEmitter.Emit(node, ctx);

        ctx.Output.Append("image::");
        ctx.Output.Append(node.Target);
        ctx.Output.Append('[');
        ctx.Output.Append(node.Alt);

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(node.Width)) parts.Add(node.Width!);
        if (!string.IsNullOrEmpty(node.Height)) parts.Add(node.Height!);
        if (!string.IsNullOrEmpty(node.Link)) parts.Add($"link={node.Link}");
        foreach (var part in parts)
        {
            ctx.Output.Append(',');
            ctx.Output.Append(part);
        }

        ctx.Output.Append("]\n");
    }

    public static void EmitAudio(AudioNode node, EmitContext ctx)
    {
        BlockAttributesEmitter.Emit(node, ctx);
        ctx.Output.Append("audio::");
        ctx.Output.Append(node.Target);
        ctx.Output.Append('[');
        var attrs = new List<string>();
        if (node.Autoplay) attrs.Add("autoplay");
        if (node.Loop) attrs.Add("loop");
        if (node.Controls) attrs.Add("controls");
        if (!string.IsNullOrEmpty(node.Width)) attrs.Add($"width={node.Width}");
        ctx.Output.Append(string.Join(",", attrs));
        ctx.Output.Append("]\n");
    }

    public static void EmitVideo(VideoNode node, EmitContext ctx)
    {
        BlockAttributesEmitter.Emit(node, ctx);
        ctx.Output.Append("video::");
        ctx.Output.Append(node.Target);
        ctx.Output.Append('[');
        var attrs = new List<string>();
        if (!string.IsNullOrEmpty(node.Provider)) attrs.Add(node.Provider!);
        if (node.Autoplay) attrs.Add("autoplay");
        if (node.Loop) attrs.Add("loop");
        if (node.Controls) attrs.Add("controls");
        if (!string.IsNullOrEmpty(node.Width)) attrs.Add($"width={node.Width}");
        if (!string.IsNullOrEmpty(node.Height)) attrs.Add($"height={node.Height}");
        if (!string.IsNullOrEmpty(node.Poster)) attrs.Add($"poster={node.Poster}");
        ctx.Output.Append(string.Join(",", attrs));
        ctx.Output.Append("]\n");
    }

    public static void EmitStem(StemBlockNode node, EmitContext ctx)
    {
        if (!string.IsNullOrEmpty(node.Title))
        {
            ctx.Output.Append('.');
            ctx.Output.Append(node.Title);
            ctx.Output.Append('\n');
        }
        BlockAttributesEmitter.Emit(node, ctx);
        // Use the [latexmath]/[asciimath] block-style attribute + a literal
        // delimiter so the parser re-recognises the block as a stem block.
        ctx.Output.Append('[');
        ctx.Output.Append(node.StemType);
        ctx.Output.Append("]\n");
        ctx.Output.Append("++++\n");
        ctx.Output.Append(node.Content);
        if (node.Content.Length == 0 || node.Content[^1] != '\n')
            ctx.Output.Append('\n');
        ctx.Output.Append("++++\n");
    }
}
