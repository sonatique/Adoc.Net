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

        var subdirs = Directory.GetDirectories(dir);
        Array.Sort(subdirs, (a, b) => string.Compare(
            Path.GetFileName(a), Path.GetFileName(b), StringComparison.Ordinal));

        foreach (var subdir in subdirs)
        {
            var manifest = ExtensionManifest.Load(subdir, onWarning);
            if (manifest is null)
                continue;

            if (manifest.MinAdocNetVersion is not null)
            {
                var currentVersion = GetCurrentAdocNetVersion();
                if (!IsVersionCompatible(currentVersion, manifest.MinAdocNetVersion))
                {
                    onWarning?.Invoke(
                        $"Extension '{manifest.Name}' requires AdocNet >= {manifest.MinAdocNetVersion}, " +
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

            results.AddRange(ExtensionLoader.LoadAssembly(entryPath, onWarning));
        }

        return results;
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
