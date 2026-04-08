using AdocNet;
using AdocNet.Ast;
using AdocNet.Extensions;

namespace AdocNet.TestExtension;

/// <summary>
/// Test block processor that sets a marker ID on paragraphs.
/// Used to verify dynamic extension loading end-to-end.
/// </summary>
public sealed class TestPrefixBlockProcessor : IBlockProcessor, IExtension
{
    /// <inheritdoc />
    public string Name => "TestPrefixProcessor";

    /// <inheritdoc />
    public string Version => "1.0.0";

    /// <inheritdoc />
    public bool CanProcess(BlockNode node) => node is ParagraphNode { Id: null };

    /// <inheritdoc />
    public bool Process(BlockNode node, RenderContext context)
    {
        node.Id = "test-processed";
        return false;
    }
}
