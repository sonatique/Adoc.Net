namespace AdocNet.Extensions;

/// <summary>
/// Optional interface for processors to declare their runtime capabilities.
/// Enables cache optimizations when all registered processors are deterministic.
/// Processors that do not implement this interface are treated as non-deterministic (safe default).
/// </summary>
public interface IExtensionCapabilities
{
    /// <summary>
    /// Returns true if this processor always produces identical AST mutations
    /// for the same input AST. When all processors are deterministic, the render
    /// cache can safely be used even with extensions registered.
    /// </summary>
    bool IsDeterministic { get; }
}
