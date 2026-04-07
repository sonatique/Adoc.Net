namespace AdocNet.Extensions;

/// <summary>
/// Represents an installed extension's metadata as stored in the registry.
/// </summary>
public sealed class ExtensionInfo
{
    /// <summary>Gets the unique name identifier for the extension.</summary>
    public string Name { get; }

    /// <summary>Gets the extension version string (e.g. "1.0.0").</summary>
    public string Version { get; }

    /// <summary>Gets a short human-readable description of the extension.</summary>
    public string Description { get; }

    /// <summary>Gets the absolute path to the installed extension directory.</summary>
    public string InstalledPath { get; }

    /// <summary>
    /// Gets the dependency specifications for this extension.
    /// Each entry is a string like "name >= version".
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; }

    /// <summary>Whether this extension is enabled for loading. Default: true.</summary>
    public bool Enabled { get; }

    /// <summary>
    /// Creates a new <see cref="ExtensionInfo"/> with the specified metadata.
    /// </summary>
    public ExtensionInfo(string name, string version, string description, string installedPath, IReadOnlyList<string> dependencies, bool enabled = true)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Version = version ?? throw new ArgumentNullException(nameof(version));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        InstalledPath = installedPath ?? throw new ArgumentNullException(nameof(installedPath));
        Dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        Enabled = enabled;
    }

    /// <summary>
    /// Returns a copy of this info with the specified enabled state.
    /// </summary>
    public ExtensionInfo WithEnabled(bool enabled)
    {
        return new ExtensionInfo(Name, Version, Description, InstalledPath, Dependencies, enabled);
    }

    /// <summary>
    /// Creates an <see cref="ExtensionInfo"/> from an <see cref="ExtensionManifest"/>,
    /// normalizing the directory path to an absolute path.
    /// </summary>
    public static ExtensionInfo FromManifest(ExtensionManifest manifest)
    {
        if (manifest is null)
            throw new ArgumentNullException(nameof(manifest));

        return new ExtensionInfo(
            name: manifest.Name,
            version: manifest.Version,
            description: manifest.Description,
            installedPath: Path.GetFullPath(manifest.DirectoryPath),
            dependencies: manifest.Dependencies);
    }

    /// <summary>
    /// Creates an <see cref="ExtensionInfo"/> from a dictionary of parsed JSON fields.
    /// Returns null if required fields are missing.
    /// </summary>
    internal static ExtensionInfo? FromDictionary(Dictionary<string, string> fields)
    {
        if (fields is null)
            return null;

        fields.TryGetValue("name", out var name);
        fields.TryGetValue("version", out var version);
        fields.TryGetValue("description", out var description);
        fields.TryGetValue("path", out var path);
        fields.TryGetValue("dependencies", out var deps);
        fields.TryGetValue("enabled", out var enabledStr);

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path))
            return null;

        var depList = ParseDependencies(deps);
        var enabled = !string.Equals(enabledStr, "false", StringComparison.OrdinalIgnoreCase);

        return new ExtensionInfo(
            name: name!.Trim(),
            version: version?.Trim() ?? "0.0.0",
            description: description?.Trim() ?? "",
            installedPath: path!.Trim(),
            dependencies: depList,
            enabled: enabled);
    }

    /// <summary>
    /// Converts the dependencies list to a comma-separated string for registry storage.
    /// </summary>
    internal string DependenciesToString()
    {
        return Dependencies.Count == 0 ? "" : string.Join(", ", Dependencies);
    }

    /// <summary>
    /// Parses a comma-separated dependency string into a list.
    /// </summary>
    internal static IReadOnlyList<string> ParseDependencies(string? deps)
    {
        if (string.IsNullOrWhiteSpace(deps))
            return Array.Empty<string>();

        var parts = deps!.Split(',');
        var result = new List<string>();
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Length > 0)
                result.Add(trimmed);
        }
        return result.ToArray();
    }
}
