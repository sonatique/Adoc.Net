using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class DocumentEmitter
{
    public static void Emit(DocumentNode doc, EmitContext ctx)
    {
        // Document header: title + attribute entries.
        if (doc.Title is not null)
        {
            ctx.Output.Append("= ");
            ctx.Output.Append(doc.Title);
            ctx.Output.Append('\n');
        }

        foreach (var attr in doc.Attributes)
        {
            ctx.Output.Append(':');
            ctx.Output.Append(attr.Key);
            ctx.Output.Append(':');
            if (!string.IsNullOrEmpty(attr.Value))
            {
                ctx.Output.Append(' ');
                ctx.Output.Append(attr.Value);
            }
            ctx.Output.Append('\n');
        }

        if (doc.Title is not null || doc.Attributes.Count > 0)
            ctx.Output.Append('\n');

        EmitBlocks(doc.Children, ctx);
    }

    public static void EmitBlocks(IReadOnlyList<AstNode> blocks, EmitContext ctx)
    {
        for (int i = 0; i < blocks.Count; i++)
        {
            AsciidocEmitter.EmitNode(blocks[i], ctx);

            // Block separator: ensure a trailing blank line between blocks.
            // The individual block emitters end on '\n' but not on '\n\n', so
            // we add the second '\n' here, except after the final block.
            if (i < blocks.Count - 1)
            {
                EnsureBlankLineSeparator(ctx);
            }
        }
    }

    private static void EnsureBlankLineSeparator(EmitContext ctx)
    {
        var sb = ctx.Output;
        // Need the buffer to end in "\n\n". Add up to two newlines as needed.
        if (sb.Length == 0)
        {
            sb.Append('\n');
            return;
        }
        if (sb[sb.Length - 1] != '\n')
            sb.Append('\n');
        if (sb.Length < 2 || sb[sb.Length - 2] != '\n')
            sb.Append('\n');
    }
}
