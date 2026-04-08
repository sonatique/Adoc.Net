namespace AdocNet.Extensions;

/// <summary>
/// Loads extensions from a structured extension directory where each subdirectory
/// contains an <c>extension.json</c> manifest and the corresponding DLL(s).
/// </summary>
public static class ExtensionDirectoryLoader
{
    /// <summary>
    /// Returns the default extension directory path (<c>~/.adocnet/extensions/</c>).
    /// </summary>
    public static string GetDefaultExtensionDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".adocnet", "extensions");
    }

    /// <summary>
    /// Scans subdirectories of <paramref name="extensionsRootDir"/> for <c>extension.json</c>
    /// manifests, validates them, checks version compatibility, and loads the entry-point DLLs.
    /// If <paramref name="extensionsRootDir"/> is null, uses the default directory.
    /// </summary>
    /// <param name="extensionsRootDir">Root directory to scan, or null for default.</param>
    /// <param name="onWarning">Optional callback for non-fatal warnings.</param>
    /// <returns>Combined list of instantiated extension objects from all valid extensions.</returns>
    public static List<object> LoadInstalledExtensions(string? extensionsRootDir, Action<string>? onWarning)
    {
        var dir = extensionsRootDir ?? GetDefaultExtensionDirectory();
        var results = new List<object>();

        if (!Directory.Exists(dir))
            return results;

        // Load registry to check enabled state
        var registryDir = Path.GetDirectoryName(dir);
        var registry = ExtensionRegistry.Load(registryDir, onWarning);

        // Pass 1: Read and validate all manifests (don't load DLLs yet)
        var subdirs = Directory.GetDirectories(dir);
        Array.Sort(subdirs, (a, b) => string.Compare(
            Path.GetFileName(a), Path.GetFileName(b), StringComparison.Ordinal));
        var validManifests = new List<ExtensionManifest>();
        var currentVersion = GetCurrentAdocNetVersion();

        foreach (var subdir in subdirs)
        {
            var manifest = ExtensionManifest.Load(subdir, onWarning);
            if (manifest is null)
                continue;

            // Skip disabled extensions
            var registryEntry = registry.Find(manifest.Name);
            if (registryEntry is not null && !registryEntry.Enabled)
                continue;

            if (manifest.MinAdocNetVersion is not null)
            {
                if (!IsVersionCompatible(currentVersion, manifest.MinAdocNetVersion))
                {
                    onWarning?.Invoke(
                        $"Extension '{manifest.Name}' requires AdocNet >= {manifest.MinAdocNetVersion}, " +
                        $"current is {currentVersion}, skipping");
                    continue;
                }
            }

            if (manifest.MaxAdocNetVersion is not null)
            {
                if (!IsVersionCompatible(manifest.MaxAdocNetVersion, currentVersion))
                {
                    onWarning?.Invoke(
                        $"Extension '{manifest.Name}' requires AdocNet <= {manifest.MaxAdocNetVersion}, " +
                        $"current is {currentVersion}, skipping");
                    continue;
                }
            }

            var entryPath = Path.Combine(subdir, manifest.Entry);
            if (!File.Exists(entryPath))
            {
                onWarning?.Invoke(
                    $"Extension '{manifest.Name}': entry DLL not found: {entryPath}");
                continue;
            }

            // Verify public key token if specified in manifest
            if (manifest.PublicKeyToken is not null)
            {
                if (!VerifyPublicKeyToken(entryPath, manifest.PublicKeyToken, manifest.Name, onWarning))
                    continue;
            }

            validManifests.Add(manifest);
        }

        // Pass 2: Sort by dependency order, then load DLLs
        var orderedManifests = ResolveLoadOrder(validManifests, onWarning);

        foreach (var manifest in orderedManifests)
        {
            var entryPath = Path.Combine(manifest.DirectoryPath, manifest.Entry);
            results.AddRange(ExtensionLoader.LoadAssembly(entryPath, onWarning));
        }

        return results;
    }

    /// <summary>
    /// Verifies that the DLL at the given path has the expected public key token.
    /// Uses AssemblyName.GetAssemblyName() to read the token without loading the assembly.
    /// </summary>
    /// <returns>True if the token matches or verification succeeds; false to skip the extension.</returns>
    private static bool VerifyPublicKeyToken(
        string entryPath, string expectedToken, string extensionName, Action<string>? onWarning)
    {
        try
        {
            var assemblyName = System.Reflection.AssemblyName.GetAssemblyName(entryPath);
            var actualBytes = assemblyName.GetPublicKeyToken();
            var actualToken = SigningHelper.ToHexString(actualBytes);

            if (actualToken.Length == 0)
            {
                onWarning?.Invoke(
                    $"Extension '{extensionName}': DLL is unsigned but manifest expects " +
                    $"publicKeyToken '{expectedToken}', skipping");
                return false;
            }

            if (!string.Equals(actualToken, expectedToken, StringComparison.OrdinalIgnoreCase))
            {
                onWarning?.Invoke(
                    $"Extension '{extensionName}': publicKeyToken mismatch — " +
                    $"expected '{expectedToken}', got '{actualToken}', skipping");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            onWarning?.Invoke(
                $"Extension '{extensionName}': failed to read assembly token: {ex.Message}, skipping");
            return false;
        }
    }

    /// <summary>
    /// Resolves the load order for a list of validated manifests using topological sort.
    /// Falls back to alphabetical order if a dependency cycle is detected.
    /// </summary>
    private static IReadOnlyList<ExtensionManifest> ResolveLoadOrder(
        List<ExtensionManifest> manifests, Action<string>? onWarning)
    {
        if (manifests.Count <= 1)
            return manifests;

        var input = new List<(string Name, IReadOnlyList<string> Dependencies)>(manifests.Count);
        foreach (var m in manifests)
        {
            // Extract dependency names from dependency specs
            var depNames = new List<string>();
            foreach (var dep in m.Dependencies)
            {
                var parsed = DependencySpec.Parse(dep);
                if (parsed is not null)
                    depNames.Add(parsed.Name);
            }
            input.Add((m.Name, depNames));
        }

        IReadOnlyList<string> order;
        try
        {
            order = DependencyResolver.Resolve(input);
        }
        catch (InvalidOperationException ex)
        {
            onWarning?.Invoke($"Dependency resolution failed: {ex.Message}. Falling back to alphabetical order.");
            manifests.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
            return manifests;
        }

        // Build lookup by name and reorder
        var byName = new Dictionary<string, ExtensionManifest>(StringComparer.Ordinal);
        foreach (var m in manifests)
            byName[m.Name] = m;

        var result = new List<ExtensionManifest>(manifests.Count);
        foreach (var name in order)
        {
            if (byName.TryGetValue(name, out var m))
                result.Add(m);
        }

        return result;
    }

    /// <summary>
    /// Gets the current AdocNet version string from the Core assembly's informational version.
    /// </summary>
    internal static string GetCurrentAdocNetVersion()
    {
        var attr = typeof(AdocEngine).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);

        if (attr.Length > 0)
        {
            var info = ((System.Reflection.AssemblyInformationalVersionAttribute)attr[0]).InformationalVersion;
            // Strip source-link hash suffix if present (e.g. "1.0.0-beta.7+abc123")
            var plusIdx = info.IndexOf('+');
            return plusIdx >= 0 ? info.Substring(0, plusIdx) : info;
        }

        var version = typeof(AdocEngine).Assembly.GetName().Version;
        return version?.ToString() ?? "0.0.0";
    }

    /// <summary>
    /// Returns true if <paramref name="current"/> >= <paramref name="minimum"/>.
    /// Handles semver prerelease suffixes by comparing numeric parts first,
    /// then treating release as newer than any prerelease of the same version.
    /// </summary>
    internal static bool IsVersionCompatible(string current, string minimum)
    {
        if (string.IsNullOrWhiteSpace(minimum))
            return true;

        ParseVersion(current, out var curNumeric, out var curPrerelease);
        ParseVersion(minimum, out var minNumeric, out var minPrerelease);

        if (!Version.TryParse(curNumeric, out var curVer))
            return false;
        if (!Version.TryParse(minNumeric, out var minVer))
            return true; // unparseable minimum — allow

        var cmp = curVer.CompareTo(minVer);
        if (cmp != 0)
            return cmp > 0;

        // Same numeric version — compare prerelease
        // Release (no prerelease) > any prerelease
        if (curPrerelease is null && minPrerelease is null)
            return true;
        if (curPrerelease is null)
            return true; // current is release, minimum is prerelease
        if (minPrerelease is null)
            return false; // current is prerelease, minimum is release

        return string.Compare(curPrerelease, minPrerelease, StringComparison.Ordinal) >= 0;
    }

    /// <summary>
    /// Returns true if the extension's declared API version is compatible with the host.
    /// Compatible when: extension major == host major and extension minor &lt;= host minor.
    /// A null extension API version is always compatible (pre-beta.9 extension).
    /// </summary>
    internal static bool IsApiVersionCompatible(string hostApiVersion, string? extensionApiVersion)
    {
        if (extensionApiVersion is null)
            return true;

        var hostParts = hostApiVersion.Split('.');
        var extParts = extensionApiVersion.Split('.');

        if (hostParts.Length < 2 || extParts.Length < 2)
            return false;

        if (!int.TryParse(hostParts[0], out var hostMajor) || !int.TryParse(hostParts[1], out var hostMinor))
            return false;
        if (!int.TryParse(extParts[0], out var extMajor) || !int.TryParse(extParts[1], out var extMinor))
            return false;

        return extMajor == hostMajor && extMinor <= hostMinor;
    }

    private static void ParseVersion(string version, out string numeric, out string? prerelease)
    {
        var dashIdx = version.IndexOf('-');
        if (dashIdx >= 0)
        {
            numeric = version.Substring(0, dashIdx);
            prerelease = version.Substring(dashIdx + 1);
        }
        else
        {
            numeric = version;
            prerelease = null;
        }
    }
}
