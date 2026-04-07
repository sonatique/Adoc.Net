namespace AdocNet.Extensions;

/// <summary>
/// Optional lifecycle interface for extensions that hold resources
/// (file handles, HTTP clients, temp directories). Extensions that do not
/// hold resources need not implement this interface.
/// </summary>
/// <remarks>
/// This is a custom interface, not <see cref="System.IDisposable"/>.
/// <see cref="Initialize"/> is called after instantiation during extension loading.
/// <see cref="Dispose"/> is called when <c>AdocEngine.Shutdown()</c> is invoked.
/// </remarks>
public interface IExtensionLifecycle
{
    /// <summary>
    /// Called after the extension is instantiated and registered.
    /// Use for one-time initialization (open connections, create temp dirs, etc.).
    /// </summary>
    void Initialize();

    /// <summary>
    /// Called when the engine is shutting down.
    /// Use to release resources (close connections, delete temp files, etc.).
    /// </summary>
    void Dispose();
}
