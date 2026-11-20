using System.Diagnostics.CodeAnalysis;

namespace AdocNet.Ast;

/// <summary>
/// A hard line break: a line ending in <c>" +"</c>, every line break under <c>%hardbreaks</c>,
/// or a Markdown hard break. Its <see cref="TextInlineNode.Value"/> is the line feed it ends
/// (<c>"\n"</c>), or empty for a <c>" +"</c> at the very end of the text, so code that only
/// knows <see cref="TextInlineNode"/> still sees the line break.
/// </summary>
public sealed class HardBreakInlineNode : TextInlineNode
{
    [SetsRequiredMembers]
    public HardBreakInlineNode()
    {
        Value = "\n";
    }
}
