using AdocNet.Ast;
using AdocNet.Caching;
using AdocNet.Extensions;

namespace AdocNet;

public sealed partial class AdocEngine
{
    private bool _enableCaching;
    private int _maxCacheEntries = 16;
    private bool? _allProcessorsDeterministic;
    private LruCache<string, DocumentNode>? _parseCache;
    private LruCache<string, byte[]>? _renderCache;
    private PersistentCacheStore? _persistentCache;

    /// <summary>
    /// Enables parse and render caching. When true, repeated <see cref="Convert"/> calls
    /// with the same input and options return cached results.
    /// Default: false (opt-in). Setting to false clears all caches.
    /// </summary>
    public bool EnableCaching
    {
        get => _enableCaching;
        set
        {
            _enableCaching = value;
            if (!value)
            {
                _parseCache?.Clear();
                _renderCache?.Clear();
                _parseCache = null;
                _renderCache = null;
            }
        }
    }

    /// <summary>
    /// Maximum number of entries in each cache (parse and render caches are sized independently).
    /// Default: 16. Minimum: 1. Uses LRU eviction when full.
    /// </summary>
    public int MaxCacheEntries
    {
        get => _maxCacheEntries;
        set
        {
            if (value < 1)
                throw new ArgumentOutOfRangeException(nameof(value), "MaxCacheEntries must be at least 1.");
            _maxCacheEntries = value;
            if (_parseCache is not null) _parseCache.Capacity = value;
            if (_renderCache is not null) _renderCache.Capacity = value;
        }
    }

    /// <summary>
    /// Enables persistent (disk-based) render caching. When true, render cache entries are
    /// written to disk and survive across sessions. Requires <see cref="EnableCaching"/> to also be true.
    /// Default: false.
    /// </summary>
    public bool EnablePersistentCache { get; set; }

    /// <summary>
    /// Directory path for persistent cache files. When null, uses the default
    /// location (<c>~/.adocnet/cache/</c>).
    /// </summary>
    public string? PersistentCacheDirectory { get; set; }

    /// <summary>
    /// Maximum number of persistent cache files on disk.
    /// Oldest files (by last write time) are evicted when exceeded.
    /// Default: 256.
    /// </summary>
    public int MaxPersistentCacheEntries { get; set; } = 256;

    /// <summary>
    /// Clears all cached parse results and render outputs.
    /// Call this if external state affecting extensions has changed.
    /// </summary>
    public void ClearCache()
    {
        _parseCache?.Clear();
        _renderCache?.Clear();
        _persistentCache?.Clear();
    }

    private void ClearCacheInternal()
    {
        _parseCache?.Clear();
        _renderCache?.Clear();
    }

    private void EnsureCaches()
    {
        _parseCache ??= new LruCache<string, DocumentNode>(_maxCacheEntries);
        _renderCache ??= new LruCache<string, byte[]>(_maxCacheEntries);
    }

    private PersistentCacheStore? GetPersistentCache()
    {
        if (!EnablePersistentCache || !_enableCaching)
            return null;

        if (_persistentCache is not null)
            return _persistentCache;

        var dir = PersistentCacheDirectory ?? GetDefaultPersistentCacheDirectory();
        var version = ExtensionDirectoryLoader.GetCurrentAdocNetVersion();
        _persistentCache = new PersistentCacheStore(dir, version, MaxPersistentCacheEntries);
        return _persistentCache;
    }

    private static string GetDefaultPersistentCacheDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".adocnet", "cache");
    }

    private bool CanUseRenderCache()
    {
        if (_documentProcessors.Count == 0 &&
            _blockProcessors.Count == 0 &&
            _inlineProcessors.Count == 0)
            return true;

        return AreAllProcessorsDeterministic();
    }

    private bool AreAllProcessorsDeterministic()
    {
        if (_allProcessorsDeterministic.HasValue)
            return _allProcessorsDeterministic.Value;

        var result = true;
        foreach (var p in _documentProcessors)
        {
            if (p is not IExtensionCapabilities caps || !caps.IsDeterministic)
            { result = false; break; }
        }

        if (result)
        {
            foreach (var p in _blockProcessors)
            {
                if (p is not IExtensionCapabilities caps || !caps.IsDeterministic)
                { result = false; break; }
            }
        }

        if (result)
        {
            foreach (var p in _inlineProcessors)
            {
                if (p is not IExtensionCapabilities caps || !caps.IsDeterministic)
                { result = false; break; }
            }
        }

        _allProcessorsDeterministic = result;
        return result;
    }

    private void ConvertUncached(string input, Stream output, RenderOptions opts)
    {
        var doc = Parser(input);
        RunExtensions(doc, opts);

        if (_outputProcessors.Count == 0)
        {
            Renderer.Render(doc, output, opts);
            return;
        }

        using var buffer = new MemoryStream();
        Renderer.Render(doc, buffer, opts);
        var bytes = RunOutputProcessors(buffer.ToArray());
        output.Write(bytes, 0, bytes.Length);
    }

    private byte[] RunOutputProcessors(byte[] rendered)
    {
        var result = rendered;
        foreach (var processor in _outputProcessors)
        {
            try
            {
                result = processor.Process(result, Renderer.Format);
            }
            catch (Exception ex)
            {
                OnWarning?.Invoke($"Output processor {processor.GetType().Name} failed: {ex.Message}");
            }
        }
        return result;
    }
}
