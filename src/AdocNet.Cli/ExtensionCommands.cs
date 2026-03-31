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
            return new CliArgs.Error("Usage: adocnet ext <list|install|remove|info|search|status> [args]");

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

            default:
                return new CliArgs.Error($"Unknown ext command: {action}. Available: list, install, remove, info, search, status.");
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
            Console.WriteLine($"  {m.Name.PadRight(nameWidth)}  {m.Version.PadRight(versionWidth)}  {m.Description}");
        }

        Console.WriteLine();
        Console.WriteLine($"{manifests.Count} extension(s) installed.");
        return ExitSuccess;
    }

    private static int ExecuteInstall(string sourcePath, bool force)
    {
        var fullSource = Path.GetFullPath(sourcePath);

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
        Console.WriteLine($"{results.Count} extension(s): {loaded} loaded, {failed} failed, {incompatible} incompatible.");
        return ExitSuccess;
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
