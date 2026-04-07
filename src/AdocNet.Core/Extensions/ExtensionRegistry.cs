namespace AdocNet.Extensions;

/// <summary>
/// Manages the local extension registry (<c>registry.json</c>).
/// The registry is a cached index of installed extensions — the filesystem
/// (<c>~/.adocnet/extensions/</c>) is always the source of truth.
/// </summary>
public sealed class ExtensionRegistry
{
    private const string RegistryFileName = "registry.json";
    private const string RegistryVersion = "1";
    private static readonly string[] FieldOrder = { "name", "version", "description", "path", "dependencies", "enabled" };

    private readonly List<ExtensionInfo> _extensions = new();
    private readonly string _registryDir;
    private readonly Action<string>? _onWarning;

    private ExtensionRegistry(string registryDir, Action<string>? onWarning)
    {
        _registryDir = registryDir;
        _onWarning = onWarning;
    }

    /// <summary>
    /// Loads the registry from the specified directory, or rebuilds it if missing/corrupt.
    /// If <paramref name="registryDir"/> is null, uses the default <c>~/.adocnet/</c> directory.
    /// </summary>
    /// <param name="registryDir">Directory containing <c>registry.json</c>, or null for default.</param>
    /// <param name="onWarning">Optional callback for non-fatal warnings.</param>
    /// <returns>A loaded or rebuilt registry.</returns>
    public static ExtensionRegistry Load(string? registryDir, Action<string>? onWarning)
    {
        var dir = registryDir ?? GetDefaultRegistryDir();
        var registry = new ExtensionRegistry(dir, onWarning);
        var registryPath = Path.Combine(dir, RegistryFileName);

        if (!File.Exists(registryPath))
            return Rebuild(dir, onWarning);

        string json;
        try
        {
            json = File.ReadAllText(registryPath);
        }
        catch (Exception ex)
        {
            onWarning?.Invoke($"Failed to read registry: {ex.Message}");
            return new ExtensionRegistry(dir, onWarning);
        }

        Dictionary<string, string> metadata;
        List<Dictionary<string, string>> items;
        try
        {
            (metadata, items) = SimpleJsonParser.ParseObjectWithArray(json, "extensions");
        }
        catch (FormatException ex)
        {
            onWarning?.Invoke($"Registry JSON is corrupt: {ex.Message}");
            return Rebuild(dir, onWarning);
        }

        if (!metadata.TryGetValue("version", out var version) || version != RegistryVersion)
        {
            onWarning?.Invoke($"Registry version mismatch (expected '{RegistryVersion}'), rebuilding");
            return Rebuild(dir, onWarning);
        }

        foreach (var item in items)
        {
            var info = ExtensionInfo.FromDictionary(item);
            if (info is not null)
                registry._extensions.Add(info);
        }

        registry.SortExtensions();

        // Validate against filesystem
        if (registry.IsStale())
            return Rebuild(dir, onWarning);

        return registry;
    }

    /// <summary>
    /// Rebuilds the registry by scanning the extensions directory.
    /// If <paramref name="registryDir"/> is null, uses the default <c>~/.adocnet/</c> directory.
    /// </summary>
    public static ExtensionRegistry Rebuild(string? registryDir, Action<string>? onWarning)
    {
        var dir = registryDir ?? GetDefaultRegistryDir();
        var registry = new ExtensionRegistry(dir, onWarning);
        var extensionsDir = Path.Combine(dir, "extensions");

        if (!Directory.Exists(extensionsDir))
            return registry;

        var subdirs = Directory.GetDirectories(extensionsDir);
        Array.Sort(subdirs, (a, b) => string.Compare(
            Path.GetFileName(a), Path.GetFileName(b), StringComparison.Ordinal));

        foreach (var subdir in subdirs)
        {
            var manifest = ExtensionManifest.Load(subdir, onWarning);
            if (manifest is null)
                continue;

            registry._extensions.Add(ExtensionInfo.FromManifest(manifest));
        }

        registry.SortExtensions();

        try
        {
            registry.Save();
        }
        catch (Exception ex)
        {
            onWarning?.Invoke($"Failed to save rebuilt registry: {ex.Message}");
        }

        return registry;
    }

    /// <summary>
    /// Saves the registry to <c>registry.json</c> using atomic write (temp file + rename).
    /// </summary>
    public void Save()
    {
        var registryPath = Path.Combine(_registryDir, RegistryFileName);
        var tempPath = registryPath + ".tmp";

        Directory.CreateDirectory(_registryDir);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["version"] = RegistryVersion
        };

        var items = new List<Dictionary<string, string>>();
        foreach (var ext in _extensions)
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name"] = ext.Name,
                ["version"] = ext.Version,
                ["description"] = ext.Description,
                ["path"] = ext.InstalledPath,
                ["dependencies"] = ext.DependenciesToString(),
                ["enabled"] = ext.Enabled ? "true" : "false"
            };
            items.Add(dict);
        }

        var json = SimpleJsonWriter.SerializeRegistry(metadata, "extensions", items, FieldOrder);

        try
        {
            File.WriteAllText(tempPath, json);

            if (File.Exists(registryPath))
                File.Delete(registryPath);

            File.Move(tempPath, registryPath);
        }
        catch (Exception ex)
        {
            _onWarning?.Invoke($"Failed to write registry: {ex.Message}");

            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Adds or replaces an extension in the registry. Does NOT save automatically.
    /// </summary>
    public void Add(ExtensionInfo info)
    {
        if (info is null)
            throw new ArgumentNullException(nameof(info));

        Remove(info.Name);
        _extensions.Add(info);
        SortExtensions();
    }

    /// <summary>
    /// Removes an extension by name. Returns true if found and removed.
    /// Does NOT save automatically.
    /// </summary>
    public bool Remove(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        var index = _extensions.FindIndex(e =>
            string.Equals(e.Name, name, StringComparison.Ordinal));

        if (index < 0)
            return false;

        _extensions.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// Sets the enabled state for a named extension. Returns true if found and updated.
    /// Does NOT save automatically — call <see cref="Save"/> after.
    /// </summary>
    public bool SetEnabled(string name, bool enabled)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));

        var index = _extensions.FindIndex(e =>
            string.Equals(e.Name, name, StringComparison.Ordinal));

        if (index < 0)
            return false;

        _extensions[index] = _extensions[index].WithEnabled(enabled);
        return true;
    }

    /// <summary>Returns all extensions, sorted by name.</summary>
    public IReadOnlyList<ExtensionInfo> GetAll() => _extensions.AsReadOnly();

    /// <summary>Finds an extension by exact name match. Returns null if not found.</summary>
    public ExtensionInfo? Find(string name)
    {
        if (name is null)
            throw new ArgumentNullException(nameof(name));

        return _extensions.Find(e =>
            string.Equals(e.Name, name, StringComparison.Ordinal));
    }

    /// <summary>
    /// Searches extensions by keyword (case-insensitive substring match on name and description).
    /// </summary>
    public IReadOnlyList<ExtensionInfo> Search(string keyword)
    {
        if (keyword is null)
            throw new ArgumentNullException(nameof(keyword));

        var results = new List<ExtensionInfo>();
        foreach (var ext in _extensions)
        {
            if (ext.Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 ||
                ext.Description.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                results.Add(ext);
            }
        }
        return results;
    }

    private bool IsStale()
    {
        var extensionsDir = Path.Combine(_registryDir, "extensions");
        if (!Directory.Exists(extensionsDir))
            return _extensions.Count > 0;

        var subdirs = Directory.GetDirectories(extensionsDir);
        var dirNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var subdir in subdirs)
        {
            if (File.Exists(Path.Combine(subdir, "extension.json")))
                dirNames.Add(Path.GetFileName(subdir));
        }

        var registryNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ext in _extensions)
            registryNames.Add(ext.Name);

        // Check: extension on disk but not in registry, or vice versa
        if (!dirNames.SetEquals(registryNames))
            return true;

        return false;
    }

    private void SortExtensions()
    {
        _extensions.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
    }

    private static string GetDefaultRegistryDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".adocnet");
    }
}
