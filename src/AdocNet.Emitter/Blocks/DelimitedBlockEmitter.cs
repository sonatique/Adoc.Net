using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class DelimitedBlockEmitter
{
    public static void Emit(DelimitedBlockNode block, EmitContext ctx)
    {
        EmitTitle(block.Title, ctx);
        EmitAttributesLine(block, ctx);
        BlockAttributesEmitter.Emit(block, ctx);

        // Paragraph-style quote/verse: `[quote, attr, cite]\nshort body\n`
        // with no `____` fences. The parser only treats it this way when the
        // content is a single short text — preserving that shape avoids the
        // round-trip drift where the reparse turns it into a structural
        // quote with a Paragraph child.
        bool isParagraphQuoteVerse = (block.BlockKind == DelimitedBlockKind.Quote
                                       || block.BlockKind == DelimitedBlockKind.Verse)
            && block.Content is not null
            && block.Children.Count == 0;

        if (isParagraphQuoteVerse)
        {
            AppendVerbatim(block.Content!, ctx);
            if (block.Content!.Length == 0 || block.Content[^1] != '\n')
                ctx.Output.Append('\n');
            return;
        }

        var (open, close) = DelimiterFor(block.BlockKind);
        ctx.Output.Append(open);
        ctx.Output.Append('\n');

        // Some blocks (e.g. simple quote blocks) carry raw Content even though
        // they're nominally structural. Prefer Content when present (most
        // faithful), fall back to walking Children. AdocNet's conditional
        // preprocessor strips backslash escapes from <c>\ifdef::</c>,
        // <c>\endif::</c>, and <c>\include::</c> at parse time even when
        // they appear inside `----` fences, so AppendVerbatim re-adds them
        // for round-trip safety.
        if (block.Content is not null)
        {
            AppendVerbatim(block.Content, ctx);
            if (block.Content.Length == 0 || block.Content[^1] != '\n')
                ctx.Output.Append('\n');
        }
        else
        {
            DocumentEmitter.EmitBlocks(block.Children, ctx);
            if (ctx.Output.Length > 0 && ctx.Output[ctx.Output.Length - 1] != '\n')
                ctx.Output.Append('\n');
        }

        ctx.Output.Append(close);
        ctx.Output.Append('\n');

        // Callout explanations follow the closing fence:
        //   <1> Explanation for callout 1
        //   <2> Explanation for callout 2
        // Each one occupies its own block-level line. The numbered markers
        // (e.g. `<1>`) are already embedded in the verbatim source content.
        if (block.Callouts is { Count: > 0 } callouts)
        {
            foreach (var co in callouts)
            {
                ctx.Output.Append('<');
                ctx.Output.Append(co.Number);
                ctx.Output.Append("> ");
                ctx.Output.Append(co.Text);
                if (co.Text.Length == 0 || co.Text[^1] != '\n')
                    ctx.Output.Append('\n');
            }
        }
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

    /// <summary>
    /// Appends verbatim content while re-adding backslash escapes for any
    /// line that begins with a conditional or include directive — those are
    /// stripped by AdocNet's preprocessor at parse time even inside verbatim
    /// fences, so without the escape a round-trip would silently remove the
    /// conditional section from the block's content.
    /// </summary>
    private static void AppendVerbatim(string content, EmitContext ctx)
    {
        int i = 0;
        while (i < content.Length)
        {
            // Find end of line.
            int eol = content.IndexOf('\n', i);
            int lineEnd = eol < 0 ? content.Length : eol;
            var line = content.AsSpan(i, lineEnd - i);

            if (LineStartsWithConditionalOrInclude(line))
                ctx.Output.Append('\\');
            ctx.Output.Append(line.ToString());
            if (eol >= 0)
                ctx.Output.Append('\n');

            i = eol < 0 ? content.Length : eol + 1;
        }
    }

    private static bool LineStartsWithConditionalOrInclude(ReadOnlySpan<char> line)
    {
        return line.StartsWith("ifdef::".AsSpan(), StringComparison.Ordinal)
            || line.StartsWith("ifndef::".AsSpan(), StringComparison.Ordinal)
            || line.StartsWith("ifeval::".AsSpan(), StringComparison.Ordinal)
            || line.StartsWith("endif::".AsSpan(), StringComparison.Ordinal)
            || line.StartsWith("include::".AsSpan(), StringComparison.Ordinal);
    }
}
