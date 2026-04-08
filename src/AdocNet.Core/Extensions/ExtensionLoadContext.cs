#if NET6_0_OR_GREATER
using System.Reflection;
using System.Runtime.Loader;

namespace AdocNet.Extensions;

/// <summary>
/// Isolated assembly load context for extension DLLs.
/// Each extension loads in its own context, preventing version conflicts
/// between extensions that depend on different versions of the same library.
/// Collectible contexts support unloading for hot-reload scenarios.
/// </summary>
internal sealed class ExtensionLoadContext : AssemblyLoadContext
{
    private readonly string _extensionDirectory;

    /// <summary>
    /// Creates an isolated, collectible load context for an extension.
    /// </summary>
    /// <param name="name">Display name for diagnostics (e.g., "ext:my-extension").</param>
    /// <param name="extensionDirectory">Directory containing the extension DLL and its dependencies.</param>
    public ExtensionLoadContext(string name, string extensionDirectory)
        : base(name, isCollectible: true)
    {
        _extensionDirectory = extensionDirectory;
    }

    /// <summary>
    /// Resolves assemblies from the extension's directory first,
    /// then falls back to the default context for shared framework assemblies.
    /// Assemblies already loaded in the default context (host interfaces, runtime)
    /// are never duplicated — they resolve via the null fallback.
    /// </summary>
    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // Never load assemblies that are already available in the default context.
        // This prevents type identity issues (e.g., IBlockProcessor from the extension's
        // copy of AdocNet.Core would differ from the host's IBlockProcessor).
        try
        {
            var existing = Default.LoadFromAssemblyName(assemblyName);
            if (existing is not null)
                return null; // Use default context's version
        }
        catch (FileNotFoundException)
        {
            // Not in default context — resolve from extension directory below
        }

        var path = Path.Combine(_extensionDirectory, $"{assemblyName.Name}.dll");
        if (File.Exists(path))
            return LoadFromAssemblyPath(Path.GetFullPath(path));

        // Fall back to default context (shared framework and runtime assemblies)
        return null;
    }
}
#endif
