using System.Collections.Generic;
using AdocNet.Ast;

namespace AdocNet.Emitter;

internal static class DocumentEmitter
{
    public static void Emit(DocumentNode doc, EmitContext ctx)
    {
        // Document header: title + user-set attribute entries. We skip the
        // attributes the parser auto-injects (backend, doctype, doc dates,
        // entity-name attributes like {cpp}/{deg}/…) because they'll be
        // re-injected when the emitted source is re-parsed; emitting them
        // explicitly only causes round-trip drift when their values are
        // re-applied during attribute substitution.
        if (doc.Title is not null)
        {
            ctx.Output.Append("= ");
            ctx.Output.Append(doc.Title);
            ctx.Output.Append('\n');
        }

        bool emittedAnyAttribute = false;
        foreach (var attr in doc.Attributes)
        {
            if (IsAutoInjectedAttribute(attr.Key)) continue;
            ctx.Output.Append(':');
            ctx.Output.Append(attr.Key);
            ctx.Output.Append(':');
            if (!string.IsNullOrEmpty(attr.Value))
            {
                ctx.Output.Append(' ');
                ctx.Output.Append(attr.Value);
            }
            ctx.Output.Append('\n');
            emittedAnyAttribute = true;
        }

        if (doc.Title is not null || emittedAnyAttribute)
            ctx.Output.Append('\n');

        EmitBlocks(doc.Children, ctx);
    }

    // Attribute names that <c>AdocNet.Parser.AdocParser</c> populates for
    // every document regardless of source content. Re-emitting them would
    // shadow the parser's own injection logic — and worse, their values
    // (dates, environment-derived strings) drift between emit and re-parse,
    // breaking structural round-trip.
    private static readonly HashSet<string> AutoInjectedAttributes = new(StringComparer.Ordinal)
    {
        // Backend / output target
        "backend", "doctype", "filetype", "outfilesuffix",
        // Entity-name character attributes
        "empty", "sp", "blank", "zwsp", "wj",
        "apos", "quot", "lsquo", "rsquo", "ldquo", "rdquo",
        "deg", "plus", "brvbar", "nbsp",
        "startsb", "endsb", "caret", "tilde",
        "backslash", "backtick", "vbar",
        "amp", "lt", "gt", "asterisk",
        "two-colons", "two-semicolons",
        "cpp",
        // Versioning / smartquotes etc.
        "asciidoc-version", "smartquotes",
        // Date / time
        "docyear", "docdate", "doctime", "docdatetime",
        "localyear", "localdate", "localtime", "localdatetime",
    };

    private static bool IsAutoInjectedAttribute(string name)
        => AutoInjectedAttributes.Contains(name);

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
