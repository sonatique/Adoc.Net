using AdocNet.Extensions;

namespace AdocNet.Cli;

/// <summary>
/// Implements the <c>adocnet ext list|install|remove</c> subcommands.
/// </summary>
internal sealed class ExtensionCommands
{
    private const int ExitSuccess = 0;
    private const int ExitError = 1;

    internal static CliArgs ParseExtArguments(string[] args)
    {
        if (args.Length < 2)
            return new CliArgs.Error("Usage: adocnet ext <list|install|remove> [args]");

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

            default:
                return new CliArgs.Error($"Unknown ext command: {action}. Available: list, install, remove.");
        }
    }

    public int Execute(CliArgs.Ext args) => args switch
    {
        CliArgs.Ext.ExtList => ExecuteList(),
        CliArgs.Ext.ExtInstall install => ExecuteInstall(install.SourcePath, install.Force),
        CliArgs.Ext.ExtRemove remove => ExecuteRemove(remove.Name),
        _ => ExitError,
    };

    private static int ExecuteList()
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

        var manifests = new List<ExtensionManifest>();
        foreach (var subdir in subdirs)
        {
            var manifest = ExtensionManifest.Load(subdir, msg => Console.Error.WriteLine($"Warning: {msg}"));
            if (manifest is not null)
                manifests.Add(manifest);
        }

        if (manifests.Count == 0)
        {
            Console.WriteLine("No extensions installed.");
            return ExitSuccess;
        }

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
        Console.WriteLine($"Removed extension '{name}'.");
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
