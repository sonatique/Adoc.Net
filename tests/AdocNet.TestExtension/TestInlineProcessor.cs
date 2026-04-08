using AdocNet;
using AdocNet.Ast;
using AdocNet.Extensions;

namespace AdocNet.TestExtension;

/// <summary>
/// Test inline processor that replaces text nodes with upper-cased versions.
/// </summary>
public sealed class TestInlineProcessor : IInlineProcessor
{
    /// <inheritdoc />
    public bool CanProcess(InlineNode node)
        => node is TextInlineNode t && t.Value != t.Value.ToUpperInvariant();

    /// <inheritdoc />
    public bool Process(InlineNode node, RenderContext context)
    {
        var text = (TextInlineNode)node;
        var upper = new TextInlineNode { Value = text.Value.ToUpperInvariant() };
        var replacements = context.GetOrCreate(() => new NodeReplacements());
        replacements.Replace(node, upper);
        return false;
    }
}
