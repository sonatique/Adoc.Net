using AdocNet.Extensions;

namespace AdocNet;

public sealed partial class AdocEngine
{
    private bool _enableHotReload;
#if NET6_0_OR_GREATER
    private readonly List<ExtensionHotReloader> _hotReloaders = new();
    private readonly object _reloadLock = new();
#endif

    /// <summary>
    /// Enables hot-reloading of extensions. When true, the engine watches extension
    /// directories for DLL changes and automatically reloads modified extensions.
    /// Default: false. Only available on .NET 6.0 or later; setting to true on
    /// netstandard2.0 throws <see cref="NotSupportedException"/>.
    /// </summary>
    public bool EnableHotReload
    {
        get => _enableHotReload;
        set
        {
#if NET6_0_OR_GREATER
            _enableHotReload = value;
            if (!value)
                StopAllWatchers();
#else
            if (value)
                throw new NotSupportedException(
                    "Hot-reload requires .NET 6.0 or later for assembly unloading.");
            _enableHotReload = false;
#endif
        }
    }

#if NET6_0_OR_GREATER
    /// <summary>
    /// Starts watching a directory for DLL changes. Called internally when extensions
    /// are loaded from a directory with hot-reload enabled.
    /// </summary>
    internal void StartWatching(string directoryPath)
    {
        if (!_enableHotReload)
            return;

        var fullPath = Path.GetFullPath(directoryPath);
        if (!Directory.Exists(fullPath))
            return;

        var reloader = new ExtensionHotReloader(this, fullPath, OnWarning);
        _hotReloaders.Add(reloader);
    }

    /// <summary>
    /// Reloads extensions from a directory. Called by <see cref="ExtensionHotReloader"/>
    /// after a debounced DLL change is detected.
    /// Thread-safe: acquires <c>_reloadLock</c> to prevent concurrent Convert + reload.
    /// </summary>
    internal void ReloadExtensions(string extensionDirectory)
    {
        lock (_reloadLock)
        {
            // 1. Shutdown lifecycle extensions
            foreach (var lifecycle in _lifecycleExtensions)
            {
                try { lifecycle.Dispose(); }
                catch (Exception ex)
                {
                    OnWarning?.Invoke($"Extension lifecycle Dispose failed during reload: {ex.Message}");
                }
            }

            // 2. Clear all processor lists
            _documentProcessors.Clear();
            _blockProcessors.Clear();
            _inlineProcessors.Clear();
            _outputProcessors.Clear();
            _lifecycleExtensions.Clear();
            _failureCounts.Clear();
            _disabledProcessors.Clear();
            _allProcessorsDeterministic = null;

            // 3. Unload old contexts
            UnloadAllExtensionContexts();

            // 4. Unfreeze to allow re-registration
            _frozen = false;

            // 5. Reload from directory
            var (extensions, contexts) = ExtensionLoader.LoadDirectoryIsolated(extensionDirectory, OnWarning);
            _loadContexts.AddRange(contexts);
            RegisterExtensions(extensions);

            // 6. Re-freeze and sort
            _frozen = true;
            SortByPriority(_documentProcessors);
            SortByPriority(_blockProcessors);
            SortByPriority(_inlineProcessors);

            // 7. Invalidate caches
            ClearCache();

            OnWarning?.Invoke($"Hot-reload: reloaded extensions from {extensionDirectory}");
        }
    }

    private void StopAllWatchers()
    {
        foreach (var reloader in _hotReloaders)
            reloader.Dispose();
        _hotReloaders.Clear();
    }
#endif
}
