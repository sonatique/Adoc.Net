namespace AdocNet.Extensions;

/// <summary>
/// Represents a parsed dependency specification from an extension manifest.
/// Format: "name >= version" or just "name" (any version).
/// </summary>
public sealed class DependencySpec
{
    /// <summary>Gets the required extension name.</summary>
    public string Name { get; }

    /// <summary>Gets the minimum required version, or null if any version is acceptable.</summary>
    public string? MinVersion { get; }

    /// <summary>
    /// Creates a new <see cref="DependencySpec"/> with the specified name and optional minimum version.
    /// </summary>
    public DependencySpec(string name, string? minVersion)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        MinVersion = minVersion;
    }

    /// <summary>
    /// Parses a dependency string like "name >= version" or "name".
    /// Returns null if the format is invalid or empty.
    /// </summary>
    public static DependencySpec? Parse(string spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return null;

        var trimmed = spec.Trim();
        var geIdx = trimmed.IndexOf(">=", StringComparison.Ordinal);

        if (geIdx >= 0)
        {
            var name = trimmed.Substring(0, geIdx).Trim();
            var version = trimmed.Substring(geIdx + 2).Trim();

            if (name.Length == 0)
                return null;

            return new DependencySpec(name, version.Length > 0 ? version : null);
        }

        // No version constraint — just a name
        return new DependencySpec(trimmed, null);
    }
}
