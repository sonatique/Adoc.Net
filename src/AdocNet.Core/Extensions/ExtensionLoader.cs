using System.Reflection;

namespace AdocNet.Extensions;

/// <summary>
/// Discovers and instantiates extension types from external assemblies.
/// Scans for types implementing <see cref="IDocumentProcessor"/>,
/// <see cref="IBlockProcessor"/>, or <see cref="IInlineProcessor"/>.
/// </summary>
public static class ExtensionLoader
{
    /// <summary>
    /// Loads extensions from a single assembly file. Discovers public types that implement
    /// processor interfaces, have parameterless constructors, and instantiates them.
    /// </summary>
    /// <param name="assemblyPath">Absolute or relative path to the assembly DLL.</param>
    /// <param name="onWarning">Optional callback for non-fatal warnings.</param>
    /// <returns>List of instantiated extension objects (each implements at least one processor interface).</returns>
    public static List<object> LoadAssembly(string assemblyPath, Action<string>? onWarning)
    {
        var results = new List<object>();

        if (assemblyPath is null)
            throw new ArgumentNullException(nameof(assemblyPath));

        var fullPath = Path.GetFullPath(assemblyPath);

        if (!File.Exists(fullPath))
        {
            onWarning?.Invoke($"Extension not found: {fullPath}");
            return results;
        }

        Assembly assembly;
        try
        {
#if NET6_0_OR_GREATER
            // Check if this assembly is already loaded in the default context.
            // Loading a host assembly (e.g. AdocNet.Core) in a separate ALC would
            // create duplicate types, breaking interface assignability checks.
            var assemblyName = System.Reflection.AssemblyName.GetAssemblyName(fullPath);
            var defaultLoaded = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => string.Equals(a.GetName().Name, assemblyName.Name, StringComparison.Ordinal));
            if (defaultLoaded is not null)
            {
                assembly = defaultLoaded;
            }
            else
            {
                var dir = Path.GetDirectoryName(fullPath) ?? ".";
                var contextName = $"ext:{Path.GetFileNameWithoutExtension(fullPath)}";
                var context = new ExtensionLoadContext(contextName, dir);
                assembly = context.LoadFromAssemblyPath(fullPath);
            }
#else
            assembly = Assembly.LoadFrom(fullPath);
#endif
        }
        catch (BadImageFormatException)
        {
            onWarning?.Invoke($"Not a valid .NET assembly: {fullPath}");
            return results;
        }
        catch (FileNotFoundException ex)
        {
            onWarning?.Invoke($"Failed to load assembly {fullPath}: {ex.Message}");
            return results;
        }

        Type[] types;
        try
        {
            types = assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            var failCount = ex.LoaderExceptions?.Length ?? 0;
            onWarning?.Invoke($"Partial load of {fullPath}: {failCount} type(s) failed to load");
#if NET5_0_OR_GREATER
            types = ex.Types.Where(t => t is not null).ToArray()!;
#else
            types = ex.Types.Where(t => t != null).ToArray();
#endif
        }

        // Sort types by FullName for deterministic order
        var processorTypes = types
            .Where(t => !t.IsAbstract && !t.IsInterface)
            .Where(IsProcessorType)
            .OrderBy(t => t.FullName, StringComparer.Ordinal);

        foreach (var type in processorTypes)
        {
            if (type.GetConstructor(Type.EmptyTypes) is null)
            {
                var name = GetExtensionDisplayName(type);
                onWarning?.Invoke($"Skipping {name} ({type.FullName}): no parameterless constructor");
                continue;
            }

            object instance;
            try
            {
                instance = Activator.CreateInstance(type)!;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie ? tie.InnerException ?? ex : ex;
                var name = GetExtensionDisplayName(type);
                onWarning?.Invoke($"Failed to instantiate {name} ({type.FullName}): {inner.Message}");
                continue;
            }

            results.Add(instance);
        }

        return results;
    }

    /// <summary>
    /// Loads extensions from all <c>*.dll</c> files in the specified directory.
    /// DLLs are loaded in alphabetical order by filename for deterministic behavior.
    /// </summary>
    /// <param name="directoryPath">Path to the directory containing extension DLLs.</param>
    /// <param name="onWarning">Optional callback for non-fatal warnings.</param>
    /// <returns>Combined list of instantiated extension objects from all DLLs.</returns>
    public static List<object> LoadDirectory(string directoryPath, Action<string>? onWarning)
    {
        var results = new List<object>();

        if (directoryPath is null)
            throw new ArgumentNullException(nameof(directoryPath));

        var fullPath = Path.GetFullPath(directoryPath);

        if (!Directory.Exists(fullPath))
        {
            onWarning?.Invoke($"Extension directory not found: {fullPath}");
            return results;
        }

        var dlls = Directory.GetFiles(fullPath, "*.dll")
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
            .ToArray();

        if (dlls.Length == 0)
        {
            onWarning?.Invoke($"No extension DLLs found in: {fullPath}");
            return results;
        }

        foreach (var dll in dlls)
        {
            results.AddRange(LoadAssembly(dll, onWarning));
        }

        return results;
    }

#if NET6_0_OR_GREATER
    /// <summary>
    /// Loads extensions from a single assembly file using an isolated load context.
    /// Returns both the extensions and the context for future unloading.
    /// </summary>
    internal static (List<object> extensions, ExtensionLoadContext? context) LoadAssemblyIsolated(
        string assemblyPath, Action<string>? onWarning)
    {
        var extensions = LoadAssembly(assemblyPath, onWarning);
        // The context was already created inside LoadAssembly — we need to retrieve it.
        // Since the assembly is already loaded, find it from its context.
        if (extensions.Count > 0)
        {
            var asm = extensions[0].GetType().Assembly;
            var ctx = System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(asm);
            if (ctx is ExtensionLoadContext elc)
                return (extensions, elc);
        }
        return (extensions, null);
    }

    /// <summary>
    /// Loads extensions from all DLLs in a directory, returning contexts for each assembly.
    /// </summary>
    internal static (List<object> extensions, List<ExtensionLoadContext> contexts) LoadDirectoryIsolated(
        string directoryPath, Action<string>? onWarning)
    {
        var allExtensions = new List<object>();
        var contexts = new List<ExtensionLoadContext>();

        if (directoryPath is null)
            throw new ArgumentNullException(nameof(directoryPath));

        var fullPath = Path.GetFullPath(directoryPath);

        if (!Directory.Exists(fullPath))
        {
            onWarning?.Invoke($"Extension directory not found: {fullPath}");
            return (allExtensions, contexts);
        }

        var dlls = Directory.GetFiles(fullPath, "*.dll")
            .OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal)
            .ToArray();

        if (dlls.Length == 0)
        {
            onWarning?.Invoke($"No extension DLLs found in: {fullPath}");
            return (allExtensions, contexts);
        }

        foreach (var dll in dlls)
        {
            var (exts, ctx) = LoadAssemblyIsolated(dll, onWarning);
            allExtensions.AddRange(exts);
            if (ctx is not null)
                contexts.Add(ctx);
        }

        return (allExtensions, contexts);
    }
#endif

    private static bool IsProcessorType(Type type)
        => typeof(IDocumentProcessor).IsAssignableFrom(type)
        || typeof(IBlockProcessor).IsAssignableFrom(type)
        || typeof(IInlineProcessor).IsAssignableFrom(type);

    private static string GetExtensionDisplayName(Type type)
    {
        // If the type implements IExtension, try to get its name from the interface
        // But since we haven't instantiated yet, just use the type name
        return type.Name;
    }
}
