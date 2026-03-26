using AdocNet.Ast;

namespace AdocNet.Extensions;

/// <summary>
/// Example inline processor that replaces <c>icon:name[]</c> macros with Unicode symbols.
/// Demonstrates <see cref="IInlineProcessor"/> with macro matching and node replacement.
/// </summary>
public sealed class IconMacroProcessor : IInlineProcessor
{
    /// <inheritdoc />
    public bool CanProcess(InlineNode node)
        => node is InlineMacroNode { Name: "icon" };

    /// <inheritdoc />
    public void Process(InlineNode node, RenderContext context)
    {
        var macro = (InlineMacroNode)node;
        var symbol = macro.Target switch
        {
            "heart" => "\u2764",   // ❤
            "star" => "\u2605",    // ★
            "check" => "\u2713",   // ✓
            "warning" => "\u26A0", // ⚠
            _ => $"[{macro.Target}]",
        };

        var text = new TextInlineNode { Value = symbol };
        var replacements = context.GetOrCreate(() => new NodeReplacements());
        replacements.Replace(node, text);
    }
}
