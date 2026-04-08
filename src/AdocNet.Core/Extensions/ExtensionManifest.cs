namespace AdocNet.Extensions;

/// <summary>
/// Represents the parsed contents of an <c>extension.json</c> manifest file
/// that describes a packaged extension in the extension directory.
/// </summary>
public sealed class ExtensionManifest
{
    /// <summary>Gets the unique name identifier for the extension.</summary>
    public string Name { get; }

    /// <summary>Gets the extension version string (e.g. "1.0.0").</summary>
    public string Version { get; }

    /// <summary>Gets a short human-readable description of the extension.</summary>
    public string Description { get; }

    /// <summary>Gets the relative path to the entry-point DLL within the extension folder.</summary>
    public string Entry { get; }

    /// <summary>Gets the minimum compatible AdocNet version, or null if no minimum is specified.</summary>
    public string? MinAdocNetVersion { get; }

    /// <summary>Gets the full path to the extension directory containing this manifest.</summary>
    public string DirectoryPath { get; }

    /// <summary>
    /// Gets the required extension API version, or null if not specified.
    /// When null, the extension is assumed compatible (pre-beta.9 extensions).
    /// Format: "major.minor" (e.g. "1.0").
    /// </summary>
    public string? ApiVersion { get; }

    /// <summary>Gets the maximum compatible AdocNet version, or null if no maximum is specified.</summary>
    public string? MaxAdocNetVersion { get; }

    /// <summary>
    /// Gets the expected public key token of the entry DLL, or null if not specified.
    /// Format: 16-character lowercase hexadecimal string (e.g., "b77a5c561934e089").
    /// When specified, the DLL's strong-name token is verified on load.
    /// </summary>
    public string? PublicKeyToken { get; }

    /// <summary>
    /// Gets the dependency specifications for this extension.
    /// Each entry is a string like "name >= version" or just "name".
    /// </summary>
    public IReadOnlyList<string> Dependencies { get; }

    private ExtensionManifest(string name, string version, string description, string entry,
        string? minAdocNetVersion, string? maxAdocNetVersion, string directoryPath,
        string? apiVersion, IReadOnlyList<string> dependencies, string? publicKeyToken)
    {
        Name = name;
        Version = version;
        Description = description;
        Entry = entry;
        MinAdocNetVersion = minAdocNetVersion;
        MaxAdocNetVersion = maxAdocNetVersion;
        DirectoryPath = directoryPath;
        ApiVersion = apiVersion;
        Dependencies = dependencies;
        PublicKeyToken = publicKeyToken;
    }

    /// <summary>
    /// Loads and parses an <c>extension.json</c> manifest from the specified extension directory.
    /// Returns null if the manifest is missing, corrupt, or fails validation.
    /// </summary>
    /// <param name="extensionDirectory">Full path to the extension directory containing <c>extension.json</c>.</param>
    /// <param name="onWarning">Optional callback for non-fatal warnings.</param>
    /// <returns>A validated <see cref="ExtensionManifest"/>, or null if loading failed.</returns>
    public static ExtensionManifest? Load(string extensionDirectory, Action<string>? onWarning)
    {
        if (extensionDirectory is null)
            throw new ArgumentNullException(nameof(extensionDirectory));

        var dirName = Path.GetFileName(extensionDirectory);
        var manifestPath = Path.Combine(extensionDirectory, "extension.json");

        if (!File.Exists(manifestPath))
        {
            onWarning?.Invoke($"Extension '{dirName}': missing extension.json, skipping");
            return null;
        }

        string json;
        try
        {
            json = File.ReadAllText(manifestPath);
        }
        catch (Exception ex)
        {
            onWarning?.Invoke($"Extension '{dirName}': failed to read extension.json: {ex.Message}");
            return null;
        }

        return Parse(json, extensionDirectory, onWarning);
    }

    /// <summary>
    /// Parses a JSON string as an extension manifest.
    /// Returns null if the JSON is invalid or fails validation.
    /// </summary>
    /// <param name="json">The JSON content of an <c>extension.json</c> file.</param>
    /// <param name="extensionDirectory">Full path to the extension directory.</param>
    /// <param name="onWarning">Optional callback for non-fatal warnings.</param>
    /// <returns>A validated <see cref="ExtensionManifest"/>, or null if parsing failed.</returns>
    public static ExtensionManifest? Parse(string json, string extensionDirectory, Action<string>? onWarning)
    {
        if (json is null)
            throw new ArgumentNullException(nameof(json));
        if (extensionDirectory is null)
            throw new ArgumentNullException(nameof(extensionDirectory));

        var dirName = Path.GetFileName(extensionDirectory);

        Dictionary<string, string> fields;
        try
        {
            fields = SimpleJsonParser.ParseFlatObject(json);
        }
        catch (FormatException ex)
        {
            onWarning?.Invoke($"Extension '{dirName}': invalid extension.json: {ex.Message}");
            return null;
        }

        if (fields.Count == 0)
        {
            onWarning?.Invoke($"Extension '{dirName}': extension.json is empty or null");
            return null;
        }

        fields.TryGetValue("name", out var name);
        fields.TryGetValue("version", out var version);
        fields.TryGetValue("description", out var description);
        fields.TryGetValue("entry", out var entry);
        fields.TryGetValue("minAdocNetVersion", out var minVersion);
        fields.TryGetValue("maxAdocNetVersion", out var maxVersion);
        fields.TryGetValue("apiVersion", out var apiVersion);
        fields.TryGetValue("dependencies", out var depsString);
        fields.TryGetValue("publicKeyToken", out var publicKeyToken);

        if (string.IsNullOrWhiteSpace(name))
        {
            onWarning?.Invoke($"Extension '{dirName}': manifest missing required 'name' field");
            return null;
        }

        if (string.IsNullOrWhiteSpace(entry))
        {
            onWarning?.Invoke($"Extension '{dirName}': manifest missing required 'entry' field");
            return null;
        }

        // Parse dependencies: either a comma-separated string or a JSON array
        IReadOnlyList<string> dependencies;
        if (depsString is not null)
        {
            // Dependencies was a flat string (comma-separated)
            dependencies = ExtensionInfo.ParseDependencies(depsString);
        }
        else
        {
            // Try parsing as a JSON array (ParseFlatObject skips arrays)
            dependencies = ParseDependenciesArray(json);
        }

        // Validate publicKeyToken format if present
        string? validatedToken = null;
        if (!string.IsNullOrWhiteSpace(publicKeyToken))
        {
            var trimmedToken = publicKeyToken!.Trim();
            if (SigningHelper.IsValidTokenFormat(trimmedToken))
            {
                validatedToken = trimmedToken.ToLowerInvariant();
            }
            else
            {
                onWarning?.Invoke(
                    $"Extension '{dirName}': invalid publicKeyToken format '{trimmedToken}' " +
                    "(expected 16 hex characters), ignoring");
            }
        }

        return new ExtensionManifest(
            name: name!.Trim(),
            version: version?.Trim() ?? "0.0.0",
            description: description?.Trim() ?? "",
            entry: entry!.Trim(),
            minAdocNetVersion: string.IsNullOrWhiteSpace(minVersion) ? null : minVersion!.Trim(),
            maxAdocNetVersion: string.IsNullOrWhiteSpace(maxVersion) ? null : maxVersion!.Trim(),
            directoryPath: extensionDirectory,
            apiVersion: string.IsNullOrWhiteSpace(apiVersion) ? null : apiVersion!.Trim(),
            dependencies: dependencies,
            publicKeyToken: validatedToken
        );
    }

    private static IReadOnlyList<string> ParseDependenciesArray(string json)
    {
        // Find "dependencies" key and try to parse its value as a string array
        var key = "\"dependencies\"";
        var idx = json.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0)
            return Array.Empty<string>();

        idx += key.Length;
        // Skip whitespace and colon
        while (idx < json.Length && (char.IsWhiteSpace(json[idx]) || json[idx] == ':'))
            idx++;

        if (idx >= json.Length || json[idx] != '[')
            return Array.Empty<string>();

        try
        {
            return SimpleJsonParser.ParseStringArray(json, idx).ToArray();
        }
        catch (FormatException)
        {
            return Array.Empty<string>();
        }
    }
}
