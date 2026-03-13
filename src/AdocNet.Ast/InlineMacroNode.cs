namespace AdocNet.Ast;

/// <summary>
/// A generic inline macro node for built-in macros like kbd, btn, menu.
/// </summary>
public sealed class InlineMacroNode : InlineNode
{
    public override AstNodeKind Kind => AstNodeKind.InlineMacro;

    /// <summary>Macro name: "kbd", "btn", "menu", etc.</summary>
    public required string Name { get; init; }

    /// <summary>Text between macro name colon and opening bracket (for menu:Target[...]).</summary>
    public required string Target { get; init; }

    /// <summary>Text inside the brackets.</summary>
    public required string Content { get; init; }

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        yield return new("Name", Name);
        if (Target.Length > 0)
            yield return new("Target", Target);
        yield return new("Content", Content);
    }
}
