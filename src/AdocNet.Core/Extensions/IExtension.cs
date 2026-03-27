namespace AdocNet.Extensions;

/// <summary>
/// Optional metadata interface for dynamically loaded extensions.
/// Extensions that implement this interface provide a name and version for
/// identification in warnings, logging, and diagnostic output.
/// Extensions that do not implement this interface are still loaded —
/// metadata defaults to the type name and version "0.0.0".
/// </summary>
public interface IExtension
{
    /// <summary>Gets a human-readable name for the extension.</summary>
    string Name { get; }

    /// <summary>Gets the version string (e.g., "1.0.0").</summary>
    string Version { get; }
}
