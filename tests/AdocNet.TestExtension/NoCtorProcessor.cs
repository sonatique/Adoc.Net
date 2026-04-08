using AdocNet.Ast;
using AdocNet.Extensions;

namespace AdocNet.TestExtension;

/// <summary>
/// Processor with no parameterless constructor — should be skipped during dynamic loading.
/// </summary>
public sealed class NoCtorProcessor : IBlockProcessor
{
    private readonly string _required;

    /// <summary>Requires an argument, so no parameterless constructor exists.</summary>
    public NoCtorProcessor(string required)
    {
        _required = required;
    }

    /// <inheritdoc />
    public bool CanProcess(BlockNode node) => false;

    /// <inheritdoc />
    public bool Process(BlockNode node, RenderContext context) { return false; }
}
