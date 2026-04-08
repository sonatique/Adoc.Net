using System.IO.Compression;
using AdocNet.Extensions;

namespace AdocNet.Cli;

/// <summary>
/// Implements the <c>adocnet ext list|install|remove|info|search</c> subcommands.
/// </summary>
internal sealed class ExtensionCommands
{
    private const int ExitSuccess = 0;
    private const int ExitError = 1;

    internal static CliArgs ParseExtArguments(string[] args)
    {
        if (args.Length < 2)
            return new CliArgs.Error("Usage: adocnet ext <list|install|remove|info|search|status|enable|disable|validate> [args]");

        var action = args[1];

        switch (action)
        {
            case "list":
                return new CliArgs.Ext.ExtList();

            case "install":
            {
                if (args.Length < 3)
                    return new CliArgs.Error("Usage: adocnet ext install <source-path> [--force]");
                string? sourcePath = null;
                bool force = false;
                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] is "--force" or "-f")
                        force = true;
                    else if (args[i].StartsWith('-'))
                        return new CliArgs.Error($"Unknown option: {args[i]}");
                    else if (sourcePath is not null)
                        return new CliArgs.Error("Only one source path may be specified.");
                    else
                        sourcePath = args[i];
                }
                if (sourcePath is null)
                    return new CliArgs.Error("Usage: adocnet ext install <source-path> [--force]");
                return new CliArgs.Ext.ExtInstall(sourcePath, force);
            }

            case "remove":
            {
                if (args.Length < 3)
                    return new CliArgs.Error("Usage: adocnet ext remove <name>");
                if (args.Length > 3)
                    return new CliArgs.Error("Usage: adocnet ext remove <name>");
                return new CliArgs.Ext.ExtRemove(args[2]);
            }

            case "info":
            {
                if (args.Length < 3)
                    return new CliArgs.Error("Usage: adocnet ext info <name>");
                if (args.Length > 3)
                    return new CliArgs.Error("Usage: adocnet ext info <name>");
                return new CliArgs.Ext.ExtInfo(args[2]);
            }

            case "search":
            {
                if (args.Length < 3)
                    return new CliArgs.Error("Usage: adocnet ext search <keyword>");
                if (args.Length > 3)
                    return new CliArgs.Error("Usage: adocnet ext search <keyword>");
                return new CliArgs.Ext.ExtSearch(args[2]);
            }

            case "status":
                return new CliArgs.Ext.ExtStatus();

            case "enable":
            {
                if (args.Length < 3)
                    return new CliArgs.Error("Usage: adocnet ext enable <name>");
                if (args.Length > 3)
                    return new CliArgs.Error("Usage: adocnet ext enable <name>");
                return new CliArgs.Ext.ExtEnable(args[2]);
            }

            case "disable":
            {
                if (args.Length < 3)
                    return new CliArgs.Error("Usage: adocnet ext disable <name>");
                if (args.Length > 3)
                    return new CliArgs.Error("Usage: adocnet ext disable <name>");
                return new CliArgs.Ext.ExtDisable(args[2]);
            }

            case "validate":
            {
                if (args.Length < 3)
                    return new CliArgs.Error("Usage: adocnet ext validate <extension-path>");
                if (args.Length > 3)
                    return new CliArgs.Error("Usage: adocnet ext validate <extension-path>");
                return new CliArgs.Ext.ExtValidate(args[2]);
            }

            default:
                return new CliArgs.Error($"Unknown ext command: {action}. Available: list, install, remove, info, search, status, enable, disable, validate.");
        }
    }

    public int Execute(CliArgs.Ext args) => args switch
    {
        CliArgs.Ext.ExtList => ExecuteList(),
        CliArgs.Ext.ExtInstall install => ExecuteInstall(install.SourcePath, install.Force),
        CliArgs.Ext.ExtRemove remove => ExecuteRemove(remove.Name),
        CliArgs.Ext.ExtInfo info => ExecuteInfo(info.Name),
        CliArgs.Ext.ExtSearch search => ExecuteSearch(search.Keyword),
        CliArgs.Ext.ExtStatus => ExecuteStatus(),
        CliArgs.Ext.ExtEnable enable => ExecuteEnable(enable.Name),
        CliArgs.Ext.ExtDisable disable => ExecuteDisable(disable.Name),
        CliArgs.Ext.ExtValidate validate => ExecuteValidate(validate.Path),
        _ => ExitError,
    };

    private static int ExecuteList()
    {
        var registry = ExtensionRegistry.Load(null, msg => Console.Error.WriteLine($"Warning: {msg}"));
        var manifests = registry.GetAll();

        if (manifests.Count == 0)
        {
            Console.WriteLine("No extensions installed.");
            return ExitSuccess;
        }

        var extensionsDir = ExtensionDirectoryLoader.GetDefaultExtensionDirectory();

        // Calculate column widths
        var nameWidth = Math.Max(4, manifests.Max(m => m.Name.Length));
        var versionWidth = Math.Max(7, manifests.Max(m => m.Version.Length));

        Console.WriteLine($"Installed extensions ({extensionsDir}):");
        Console.WriteLine();
        Console.WriteLine($"  {"Name".PadRight(nameWidth)}  {"Version".PadRight(versionWidth)}  Description");
        Console.WriteLine($"  {new string('-', nameWidth)}  {new string('-', versionWidth)}  -----------");

        foreach (var m in manifests)
        {
            var status = m.Enabled ? "" : " [disabled]";
            Console.WriteLine($"  {m.Name.PadRight(nameWidth)}  {m.Version.PadRight(versionWidth)}  {m.Description}{status}");
        }

        Console.WriteLine();
        Console.WriteLine($"{manifests.Count} extension(s) installed.");
        return ExitSuccess;
    }

    private static int ExecuteInstall(string sourcePath, bool force)
    {
        var fullSource = Path.GetFullPath(sourcePath);

        // Zip file install
        if (fullSource.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(fullSource))
            return ExecuteInstallFromZip(fullSource, force);

        // Directory install
        return ExecuteInstallFromDirectory(fullSource, force);
    }

    private static int ExecuteInstallFromZip(string zipPath, bool force)
    {
        string? tempDir = null;
        try
        {
            tempDir = Path.Combine(Path.GetTempPath(), "adocnet-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                ZipFile.ExtractToDirectory(zipPath, tempDir);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: Failed to extract zip: {ex.Message}");
                return ExitError;
            }

            // Find manifest: at root or in single top-level subdirectory
            var sourceDir = ResolveExtractedDirectory(tempDir);
            if (sourceDir is null)
            {
                Console.Error.WriteLine("Error: No extension.json found in zip archive.");
                return ExitError;
            }

            return ExecuteInstallFromDirectory(sourceDir, force);
        }
        finally
        {
            if (tempDir is not null)
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    private static string? ResolveExtractedDirectory(string extractedDir)
    {
        if (File.Exists(Path.Combine(extractedDir, "extension.json")))
            return extractedDir;

        var subdirs = Directory.GetDirectories(extractedDir);
        if (subdirs.Length == 1 && File.Exists(Path.Combine(subdirs[0], "extension.json")))
            return subdirs[0];

        return null;
    }

    private static int ExecuteInstallFromDirectory(string fullSource, bool force)
    {
        if (!Directory.Exists(fullSource))
        {
            Console.Error.WriteLine($"Error: Source path not found: {fullSource}");
            return ExitError;
        }

        var manifestPath = Path.Combine(fullSource, "extension.json");
        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"Error: No extension.json found in {fullSource}");
            return ExitError;
        }

        var manifest = ExtensionManifest.Load(fullSource, msg => Console.Error.WriteLine($"Warning: {msg}"));
        if (manifest is null)
        {
            Console.Error.WriteLine("Error: Invalid extension.json. See warnings above.");
            return ExitError;
        }

        var extensionsDir = ExtensionDirectoryLoader.GetDefaultExtensionDirectory();
        var targetDir = Path.Combine(extensionsDir, manifest.Name);

        if (Directory.Exists(targetDir))
        {
            if (!force)
            {
                Console.Error.WriteLine(
                    $"Error: Extension '{manifest.Name}' is already installed. " +
                    $"Use --force to overwrite, or remove it first with 'adocnet ext remove {manifest.Name}'.");
                return ExitError;
            }
            Directory.Delete(targetDir, recursive: true);
        }

        Directory.CreateDirectory(targetDir);
        CopyDirectory(fullSource, targetDir);

        // Update registry
        var registry = ExtensionRegistry.Load(null, msg => Console.Error.WriteLine($"Warning: {msg}"));
        var info = ExtensionInfo.FromManifest(
            ExtensionManifest.Load(targetDir, null) ?? manifest);
        registry.Add(info);
        registry.Save();

        Console.WriteLine($"Installed extension '{manifest.Name}' v{manifest.Version}.");
        return ExitSuccess;
    }

    private static int ExecuteRemove(string name)
    {
        var extensionsDir = ExtensionDirectoryLoader.GetDefaultExtensionDirectory();
        var targetDir = Path.Combine(extensionsDir, name);

        if (!Directory.Exists(targetDir))
        {
            Console.Error.WriteLine($"Error: Extension '{name}' is not installed.");
            return ExitError;
        }

        Directory.Delete(targetDir, recursive: true);

        // Update registry
        var registry = ExtensionRegistry.Load(null, msg => Console.Error.WriteLine($"Warning: {msg}"));
        registry.Remove(name);
        registry.Save();

        Console.WriteLine($"Removed extension '{name}'.");
        return ExitSuccess;
    }

    private static int ExecuteInfo(string name)
    {
        var registry = ExtensionRegistry.Load(null, msg => Console.Error.WriteLine($"Warning: {msg}"));
        var info = registry.Find(name);

        if (info is null)
        {
            Console.Error.WriteLine($"Error: Extension '{name}' is not installed.");
            return ExitError;
        }

        Console.WriteLine($"Extension: {info.Name}");
        Console.WriteLine($"Version:   {info.Version}");
        Console.WriteLine($"Description: {info.Description}");
        Console.WriteLine($"Path:      {info.InstalledPath}");

        // Read additional details from manifest on disk
        var manifest = ExtensionManifest.Load(info.InstalledPath, null);
        if (manifest is not null)
        {
            Console.WriteLine($"Entry:     {manifest.Entry}");
            if (manifest.MinAdocNetVersion is not null)
                Console.WriteLine($"Min AdocNet: {manifest.MinAdocNetVersion}");
        }

        if (info.Dependencies.Count > 0)
        {
            Console.WriteLine("Dependencies:");
            foreach (var dep in info.Dependencies)
                Console.WriteLine($"  - {dep}");
        }

        return ExitSuccess;
    }

    private static int ExecuteSearch(string keyword)
    {
        var registry = ExtensionRegistry.Load(null, msg => Console.Error.WriteLine($"Warning: {msg}"));
        var matches = registry.Search(keyword);

        if (matches.Count == 0)
        {
            Console.WriteLine($"No extensions match '{keyword}'.");
            return ExitSuccess;
        }

        var nameWidth = Math.Max(4, matches.Max(m => m.Name.Length));
        var versionWidth = Math.Max(7, matches.Max(m => m.Version.Length));

        Console.WriteLine($"Search results for \"{keyword}\":");
        Console.WriteLine();
        Console.WriteLine($"  {"Name".PadRight(nameWidth)}  {"Version".PadRight(versionWidth)}  Description");
        Console.WriteLine($"  {new string('-', nameWidth)}  {new string('-', versionWidth)}  -----------");

        foreach (var m in matches)
        {
            Console.WriteLine($"  {m.Name.PadRight(nameWidth)}  {m.Version.PadRight(versionWidth)}  {m.Description}");
        }

        Console.WriteLine();
        Console.WriteLine($"{matches.Count} extension(s) matched.");
        return ExitSuccess;
    }

    private static int ExecuteStatus()
    {
        var extensionsDir = ExtensionDirectoryLoader.GetDefaultExtensionDirectory();

        if (!Directory.Exists(extensionsDir))
        {
            Console.WriteLine("No extensions installed.");
            return ExitSuccess;
        }

        var subdirs = Directory.GetDirectories(extensionsDir);
        Array.Sort(subdirs, (a, b) => string.Compare(
            Path.GetFileName(a), Path.GetFileName(b), StringComparison.Ordinal));

        if (subdirs.Length == 0)
        {
            Console.WriteLine("No extensions installed.");
            return ExitSuccess;
        }

        var registry = ExtensionRegistry.Load(null, msg => { });
        var results = new List<(string Name, string Version, ExtensionState State, string Reason)>();

        foreach (var subdir in subdirs)
        {
            var warnings = new List<string>();
            var manifest = ExtensionManifest.Load(subdir, msg => warnings.Add(msg));

            if (manifest is null)
            {
                var dirName = Path.GetFileName(subdir);
                results.Add((dirName, "?", ExtensionState.Failed,
                    warnings.Count > 0 ? warnings[0] : "Invalid manifest"));
                continue;
            }

            // Check enabled state from registry
            var regEntry = registry.Find(manifest.Name);
            if (regEntry is not null && !regEntry.Enabled)
            {
                results.Add((manifest.Name, manifest.Version, ExtensionState.Disabled,
                    "Disabled by user"));
                continue;
            }

            // Check API version compatibility using simple major.minor comparison
            if (manifest.ApiVersion is not null)
            {
                var hostParts = AdocEngine.ExtensionApiVersion.Split('.');
                var extParts = manifest.ApiVersion.Split('.');
                if (hostParts.Length >= 2 && extParts.Length >= 2 &&
                    int.TryParse(hostParts[0], out var hMaj) && int.TryParse(hostParts[1], out var hMin) &&
                    int.TryParse(extParts[0], out var eMaj) && int.TryParse(extParts[1], out var eMin))
                {
                    if (eMaj != hMaj || eMin > hMin)
                    {
                        results.Add((manifest.Name, manifest.Version, ExtensionState.Incompatible,
                            $"Requires API version {manifest.ApiVersion}"));
                        continue;
                    }
                }
            }

            // Try loading the entry DLL
            var entryPath = Path.Combine(subdir, manifest.Entry);
            if (!File.Exists(entryPath))
            {
                results.Add((manifest.Name, manifest.Version, ExtensionState.Failed,
                    $"Entry DLL not found: {manifest.Entry}"));
                continue;
            }

            var loadWarnings = new List<string>();
            var processors = ExtensionLoader.LoadAssembly(entryPath, msg => loadWarnings.Add(msg));

            if (processors.Count == 0 && loadWarnings.Count > 0)
            {
                results.Add((manifest.Name, manifest.Version, ExtensionState.Failed,
                    loadWarnings[0]));
            }
            else
            {
                var reason = $"{processors.Count} processor(s)";
                results.Add((manifest.Name, manifest.Version, ExtensionState.Loaded, reason));
            }
        }

        if (results.Count == 0)
        {
            Console.WriteLine("No extensions installed.");
            return ExitSuccess;
        }

        var nameWidth = Math.Max(4, results.Max(r => r.Name.Length));
        var versionWidth = Math.Max(7, results.Max(r => r.Version.Length));
        var stateWidth = Math.Max(5, results.Max(r => r.State.ToString().Length));

        Console.WriteLine($"Extension status ({extensionsDir}):");
        Console.WriteLine();
        Console.WriteLine($"  {"Name".PadRight(nameWidth)}  {"Version".PadRight(versionWidth)}  {"State".PadRight(stateWidth)}  Reason");
        Console.WriteLine($"  {new string('-', nameWidth)}  {new string('-', versionWidth)}  {new string('-', stateWidth)}  ------");

        foreach (var (name, version, state, reason) in results)
        {
            Console.WriteLine($"  {name.PadRight(nameWidth)}  {version.PadRight(versionWidth)}  {state.ToString().PadRight(stateWidth)}  {reason}");
        }

        Console.WriteLine();
        var loaded = results.Count(r => r.State == ExtensionState.Loaded);
        var failed = results.Count(r => r.State == ExtensionState.Failed);
        var incompatible = results.Count(r => r.State == ExtensionState.Incompatible);
        var disabled = results.Count(r => r.State == ExtensionState.Disabled);
        Console.WriteLine($"{results.Count} extension(s): {loaded} loaded, {failed} failed, {incompatible} incompatible, {disabled} disabled.");
        return ExitSuccess;
    }

    private static int ExecuteEnable(string name)
    {
        var registry = ExtensionRegistry.Load(null, msg => Console.Error.WriteLine($"Warning: {msg}"));
        if (!registry.SetEnabled(name, true))
        {
            Console.Error.WriteLine($"Error: Extension '{name}' is not installed.");
            return ExitError;
        }
        registry.Save();
        Console.WriteLine($"Enabled extension '{name}'.");
        return ExitSuccess;
    }

    private static int ExecuteDisable(string name)
    {
        var registry = ExtensionRegistry.Load(null, msg => Console.Error.WriteLine($"Warning: {msg}"));
        if (!registry.SetEnabled(name, false))
        {
            Console.Error.WriteLine($"Error: Extension '{name}' is not installed.");
            return ExitError;
        }
        registry.Save();
        Console.WriteLine($"Disabled extension '{name}'.");
        return ExitSuccess;
    }

    private static int ExecuteValidate(string extensionPath)
    {
        var fullPath = Path.GetFullPath(extensionPath);
        string? tempDir = null;

        try
        {
            // Handle zip files
            if (fullPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && File.Exists(fullPath))
            {
                tempDir = Path.Combine(Path.GetTempPath(), "adocnet-validate-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(tempDir);
                try
                {
                    ZipFile.ExtractToDirectory(fullPath, tempDir);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: Failed to extract zip: {ex.Message}");
                    return ExitError;
                }
                fullPath = ResolveExtractedDirectory(tempDir) ?? tempDir;
            }

            if (!Directory.Exists(fullPath))
            {
                Console.Error.WriteLine($"Error: Path not found: {fullPath}");
                return ExitError;
            }

            Console.WriteLine($"Validating extension at: {fullPath}");
            Console.WriteLine();

            var registry = ExtensionRegistry.Load(null, _ => { });
            var validator = new ExtensionValidator(registry);
            var results = validator.Validate(fullPath);

            int passed = 0, failed = 0, warned = 0, skipped = 0;

            foreach (var result in results)
            {
                var prefix = result.Status switch
                {
                    ValidationStatus.Pass => "[PASS]",
                    ValidationStatus.Fail => "[FAIL]",
                    ValidationStatus.Warn => "[WARN]",
                    ValidationStatus.Skip => "[SKIP]",
                    _ => "[????]"
                };

                Console.WriteLine($"  {prefix} {result.CheckName}: {result.Message}");

                switch (result.Status)
                {
                    case ValidationStatus.Pass: passed++; break;
                    case ValidationStatus.Fail: failed++; break;
                    case ValidationStatus.Warn: warned++; break;
                    case ValidationStatus.Skip: skipped++; break;
                }
            }

            Console.WriteLine();
            var total = passed + failed + warned + skipped;

            if (failed == 0)
            {
                Console.WriteLine($"Validation PASSED ({passed}/{total} passed, {warned} warnings, {skipped} skipped)");
                return ExitSuccess;
            }

            Console.WriteLine($"Validation FAILED ({passed}/{total} passed, {failed} failed, {warned} warnings, {skipped} skipped)");
            return ExitError;
        }
        finally
        {
            if (tempDir is not null)
            {
                try { Directory.Delete(tempDir, recursive: true); }
                catch { /* best-effort cleanup */ }
            }
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (var file in Directory.GetFiles(source))
        {
            var destFile = Path.Combine(target, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(source))
        {
            var destDir = Path.Combine(target, Path.GetFileName(dir));
            Directory.CreateDirectory(destDir);
            CopyDirectory(dir, destDir);
        }
    }
}
