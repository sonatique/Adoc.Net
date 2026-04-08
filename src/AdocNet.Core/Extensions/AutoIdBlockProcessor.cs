using AdocNet.Ast;

namespace AdocNet.Extensions;

/// <summary>
/// Example block processor that auto-generates IDs for sections that lack one.
/// Demonstrates <see cref="IBlockProcessor"/> with selective targeting and property mutation.
/// </summary>
public sealed class AutoIdBlockProcessor : IBlockProcessor
{
    private readonly string _prefix;

    /// <summary>
    /// Initializes the processor with an optional ID prefix.
    /// </summary>
    /// <param name="prefix">Prefix prepended to generated IDs (default: "_").</param>
    public AutoIdBlockProcessor(string prefix = "_")
    {
        _prefix = prefix;
    }

    /// <inheritdoc />
    public bool CanProcess(BlockNode node)
        => node is SectionNode { Id: null };

    /// <inheritdoc />
    public bool Process(BlockNode node, RenderContext context)
    {
        var section = (SectionNode)node;
        var slug = section.Title
            .ToLowerInvariant()
            .Replace(' ', '-')
            .Replace(".", "")
            .Replace(",", "");
        section.Id = _prefix + slug;
        return false;
    }
}
