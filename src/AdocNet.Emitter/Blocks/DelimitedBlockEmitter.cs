using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class DelimitedBlockEmitter
{
    public static void Emit(DelimitedBlockNode block, EmitContext ctx)
    {
        EmitTitle(block.Title, ctx);
        EmitAttributesLine(block, ctx);
        BlockAttributesEmitter.Emit(block, ctx);

        var (open, close) = DelimiterFor(block.BlockKind);
        ctx.Output.Append(open);
        ctx.Output.Append('\n');

        if (HasVerbatimContent(block.BlockKind))
        {
            // Verbatim blocks store raw content as a string.
            if (block.Content is not null)
            {
                ctx.Output.Append(block.Content);
                if (block.Content.Length == 0 || block.Content[^1] != '\n')
                    ctx.Output.Append('\n');
            }
        }
        else
        {
            // Structural blocks have parsed children.
            DocumentEmitter.EmitBlocks(block.Children, ctx);
            if (ctx.Output.Length > 0 && ctx.Output[ctx.Output.Length - 1] != '\n')
                ctx.Output.Append('\n');
        }

        ctx.Output.Append(close);
        ctx.Output.Append('\n');
    }

    private static (string Open, string Close) DelimiterFor(DelimitedBlockKind kind) => kind switch
    {
        DelimitedBlockKind.Literal => ("....", "...."),
        DelimitedBlockKind.Listing => ("----", "----"),
        DelimitedBlockKind.Source  => ("----", "----"),
        DelimitedBlockKind.Example => ("====", "===="),
        DelimitedBlockKind.Quote   => ("____", "____"),
        DelimitedBlockKind.Sidebar => ("****", "****"),
        DelimitedBlockKind.Passthrough => ("++++", "++++"),
        DelimitedBlockKind.Open    => ("--", "--"),
        DelimitedBlockKind.Verse   => ("____", "____"),
        _ => ("----", "----"),
    };

    private static bool HasVerbatimContent(DelimitedBlockKind kind) => kind switch
    {
        DelimitedBlockKind.Literal => true,
        DelimitedBlockKind.Listing => true,
        DelimitedBlockKind.Source  => true,
        DelimitedBlockKind.Passthrough => true,
        DelimitedBlockKind.Verse   => true,
        _ => false,
    };

    /// <summary>
    /// Emits the <c>[source,lang]</c>, <c>[quote,attr,cite]</c>, <c>[verse]</c>,
    /// <c>[%collapsible]</c>, etc. attribute lines that precede a delimited
    /// block, distinct from the generic id/role line emitted by
    /// <see cref="BlockAttributesEmitter"/>.
    /// </summary>
    private static void EmitAttributesLine(DelimitedBlockNode block, EmitContext ctx)
    {
        var parts = new List<string>();

        switch (block.BlockKind)
        {
            case DelimitedBlockKind.Source:
                parts.Add("source");
                if (!string.IsNullOrEmpty(block.Language)) parts.Add(block.Language!);
                break;
            case DelimitedBlockKind.Quote:
                parts.Add("quote");
                if (!string.IsNullOrEmpty(block.Attribution)) parts.Add(block.Attribution!);
                if (!string.IsNullOrEmpty(block.CitationSource)) parts.Add(block.CitationSource!);
                break;
            case DelimitedBlockKind.Verse:
                parts.Add("verse");
                if (!string.IsNullOrEmpty(block.Attribution)) parts.Add(block.Attribution!);
                if (!string.IsNullOrEmpty(block.CitationSource)) parts.Add(block.CitationSource!);
                break;
            default:
                if (!string.IsNullOrEmpty(block.Style))
                    parts.Add(block.Style!);
                break;
        }

        // [%collapsible] is conventionally appended after the style.
        string? collapsibleOption = block.IsCollapsible ? "%collapsible" : null;
        if (!string.IsNullOrEmpty(block.Highlight))
            parts.Add($"highlight=\"{block.Highlight}\"");

        if (parts.Count == 0 && collapsibleOption is null) return;

        ctx.Output.Append('[');
        if (collapsibleOption is not null)
            ctx.Output.Append(collapsibleOption);
        for (int i = 0; i < parts.Count; i++)
        {
            if (i > 0 || collapsibleOption is not null) ctx.Output.Append(',');
            ctx.Output.Append(parts[i]);
        }
        ctx.Output.Append("]\n");
    }

    private static void EmitTitle(string? title, EmitContext ctx)
    {
        if (string.IsNullOrEmpty(title)) return;
        ctx.Output.Append('.');
        ctx.Output.Append(title);
        ctx.Output.Append('\n');
    }
}
