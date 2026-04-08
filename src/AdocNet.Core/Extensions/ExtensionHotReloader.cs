#if NET6_0_OR_GREATER
namespace AdocNet.Extensions;

/// <summary>
/// Watches an extension directory for DLL changes and triggers engine reload.
/// Uses a 500ms debounce to coalesce rapid file system events (DLL writes are
/// multi-step: create, write content, close handle).
/// </summary>
internal sealed class ExtensionHotReloader : IDisposable
{
    private readonly AdocEngine _engine;
    private readonly string _extensionDirectory;
    private readonly Action<string>? _onWarning;
    private readonly FileSystemWatcher _watcher;
    private Timer? _debounceTimer;
    private bool _disposed;

    /// <summary>Debounce delay in milliseconds.</summary>
    internal const int DebounceMs = 500;

    /// <summary>
    /// Fired after a successful reload. Used by tests to synchronize.
    /// </summary>
    internal event Action? Reloaded;

    public ExtensionHotReloader(
        AdocEngine engine,
        string extensionDirectory,
        Action<string>? onWarning)
    {
        _engine = engine;
        _extensionDirectory = extensionDirectory;
        _onWarning = onWarning;

        _watcher = new FileSystemWatcher(extensionDirectory, "*.dll")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
            IncludeSubdirectories = false,
            EnableRaisingEvents = true
        };

        _watcher.Changed += OnDllChanged;
        _watcher.Created += OnDllChanged;
        _watcher.Deleted += OnDllChanged;
    }

    private void OnDllChanged(object sender, FileSystemEventArgs e)
    {
        // Reset debounce timer on every event
        _debounceTimer?.Dispose();
        _debounceTimer = new Timer(OnDebounceElapsed, null, DebounceMs, Timeout.Infinite);
    }

    private void OnDebounceElapsed(object? state)
    {
        if (_disposed) return;

        try
        {
            _engine.ReloadExtensions(_extensionDirectory);
            Reloaded?.Invoke();
        }
        catch (Exception ex)
        {
            _onWarning?.Invoke($"Hot-reload failed for {_extensionDirectory}: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnDllChanged;
        _watcher.Created -= OnDllChanged;
        _watcher.Deleted -= OnDllChanged;
        _watcher.Dispose();

        _debounceTimer?.Dispose();
        _debounceTimer = null;
    }
}
#endif
