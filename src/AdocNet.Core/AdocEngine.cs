using AdocNet.Ast;
using AdocNet.Caching;
using AdocNet.Editor;
using AdocNet.Extensions;

namespace AdocNet;

/// <summary>
/// High-level facade that combines parsing and rendering of AsciiDoc source text.
/// </summary>
public sealed class AdocEngine
{
    /// <summary>
    /// The extension API version supported by this build.
    /// Extensions declare their required API version in the manifest <c>apiVersion</c> field.
    /// Compatible when: extension major == host major and extension minor &lt;= host minor.
    /// </summary>
    public const string ExtensionApiVersion = "1.0";

    private readonly List<IDocumentProcessor> _documentProcessors = new();
    private readonly List<IBlockProcessor> _blockProcessors = new();
    private readonly List<IInlineProcessor> _inlineProcessors = new();
    private readonly List<IOutputProcessor> _outputProcessors = new();
    private readonly List<IExtensionLifecycle> _lifecycleExtensions = new();
    private readonly Dictionary<object, int> _failureCounts = new();
    private readonly HashSet<object> _disabledProcessors = new();
    private bool _frozen;
    private bool _enableCaching;
    private int _maxCacheEntries = 16;
    private LruCache<string, DocumentNode>? _parseCache;
    private LruCache<string, byte[]>? _renderCache;

    /// <summary>Gets the renderer used to produce output.</summary>
    public IDocumentRenderer Renderer { get; init; }

    /// <summary>Gets the parser function that converts AsciiDoc source text into a document AST.</summary>
    public Func<string, DocumentNode> Parser { get; init; }

    /// <summary>
    /// Optional warning callback. Invoked when a processor throws an exception
    /// or a non-fatal issue occurs during processing.
    /// When null, warnings are silently discarded.
    /// </summary>
    public Action<string>? OnWarning { get; set; }

    /// <summary>
    /// Maximum consecutive failures before a processor is disabled for this engine's lifetime.
    /// Default: 3. Set to 0 to never disable processors (beta.8 behavior).
    /// </summary>
    public int MaxProcessorFailures { get; set; } = 3;

    /// <summary>
    /// Diagnostics emitted by extensions during the most recent <see cref="Convert"/> call.
    /// Empty if no extensions ran or no diagnostics were emitted.
    /// Cleared and repopulated on each <see cref="Convert"/> call.
    /// </summary>
    public IReadOnlyList<Diagnostic> LastExtensionDiagnostics { get; private set; } = Array.Empty<Diagnostic>();

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
    /// Initializes a new <see cref="AdocEngine"/> with the specified renderer and parser.
    /// </summary>
    /// <param name="renderer">The renderer used to produce output.</param>
    /// <param name="parser">A function that parses AsciiDoc source text into a <see cref="DocumentNode"/>.</param>
    public AdocEngine(IDocumentRenderer renderer, Func<string, DocumentNode> parser)
    {
        Renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        Parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    /// <summary>
    /// Registers a document processor. Processors execute in registration order (FIFO).
    /// Must be called before the first <see cref="Convert"/> call.
    /// </summary>
    /// <param name="processor">The document processor to register.</param>
    /// <returns>This engine instance for fluent chaining.</returns>
    public AdocEngine RegisterDocumentProcessor(IDocumentProcessor processor)
    {
        ThrowIfFrozen();
        _documentProcessors.Add(processor ?? throw new ArgumentNullException(nameof(processor)));
        ClearCacheInternal();
        return this;
    }

    /// <summary>
    /// Registers a block processor. Processors execute in registration order (FIFO).
    /// Must be called before the first <see cref="Convert"/> call.
    /// </summary>
    /// <param name="processor">The block processor to register.</param>
    /// <returns>This engine instance for fluent chaining.</returns>
    public AdocEngine RegisterBlockProcessor(IBlockProcessor processor)
    {
        ThrowIfFrozen();
        _blockProcessors.Add(processor ?? throw new ArgumentNullException(nameof(processor)));
        ClearCacheInternal();
        return this;
    }

    /// <summary>
    /// Registers an inline processor. Processors execute in registration order (FIFO).
    /// Must be called before the first <see cref="Convert"/> call.
    /// </summary>
    /// <param name="processor">The inline processor to register.</param>
    /// <returns>This engine instance for fluent chaining.</returns>
    public AdocEngine RegisterInlineProcessor(IInlineProcessor processor)
    {
        ThrowIfFrozen();
        _inlineProcessors.Add(processor ?? throw new ArgumentNullException(nameof(processor)));
        ClearCacheInternal();
        return this;
    }

    /// <summary>
    /// Registers an output processor. Processors execute in registration order (FIFO)
    /// after rendering completes. Must be called before the first <see cref="Convert"/> call.
    /// </summary>
    /// <param name="processor">The output processor to register.</param>
    /// <returns>This engine instance for fluent chaining.</returns>
    public AdocEngine RegisterOutputProcessor(IOutputProcessor processor)
    {
        ThrowIfFrozen();
        _outputProcessors.Add(processor ?? throw new ArgumentNullException(nameof(processor)));
        return this;
    }

    /// <summary>
    /// Loads extensions from a single assembly file. Discovers types implementing
    /// <see cref="IDocumentProcessor"/>, <see cref="IBlockProcessor"/>, or <see cref="IInlineProcessor"/>
    /// with parameterless constructors, instantiates them, and registers them.
    /// Must be called before the first <see cref="Convert"/> call.
    /// </summary>
    /// <param name="assemblyPath">Path to the extension assembly DLL.</param>
    /// <returns>This engine instance for fluent chaining.</returns>
    public AdocEngine LoadExtension(string assemblyPath)
    {
        ThrowIfFrozen();
        var extensions = ExtensionLoader.LoadAssembly(assemblyPath, OnWarning);
        RegisterExtensions(extensions);
        return this;
    }

    /// <summary>
    /// Loads extensions from all <c>*.dll</c> files in the specified directory.
    /// DLLs are loaded in alphabetical order by filename for deterministic behavior.
    /// Must be called before the first <see cref="Convert"/> call.
    /// </summary>
    /// <param name="directoryPath">Path to the directory containing extension DLLs.</param>
    /// <returns>This engine instance for fluent chaining.</returns>
    public AdocEngine LoadExtensions(string directoryPath)
    {
        ThrowIfFrozen();
        var extensions = ExtensionLoader.LoadDirectory(directoryPath, OnWarning);
        RegisterExtensions(extensions);
        return this;
    }

    /// <summary>
    /// Loads extensions from the default extension directory (<c>~/.adocnet/extensions/</c>).
    /// Each subdirectory must contain an <c>extension.json</c> manifest.
    /// Must be called before the first <see cref="Convert"/> call.
    /// </summary>
    /// <returns>This engine instance for fluent chaining.</returns>
    public AdocEngine LoadInstalledExtensions()
    {
        ThrowIfFrozen();
        var extensions = ExtensionDirectoryLoader.LoadInstalledExtensions(null, OnWarning);
        RegisterExtensions(extensions);
        return this;
    }

    /// <summary>
    /// Loads extensions from a custom extension directory.
    /// Each subdirectory must contain an <c>extension.json</c> manifest.
    /// Must be called before the first <see cref="Convert"/> call.
    /// </summary>
    /// <param name="extensionsRootDir">Path to the directory containing extension subdirectories.</param>
    /// <returns>This engine instance for fluent chaining.</returns>
    public AdocEngine LoadInstalledExtensions(string extensionsRootDir)
    {
        ThrowIfFrozen();
        var extensions = ExtensionDirectoryLoader.LoadInstalledExtensions(extensionsRootDir, OnWarning);
        RegisterExtensions(extensions);
        return this;
    }

    /// <summary>
    /// Parses the AsciiDoc <paramref name="input"/> and writes the rendered output to <paramref name="output"/>.
    /// </summary>
    /// <param name="input">The AsciiDoc source text.</param>
    /// <param name="output">The stream to write the rendered output to.</param>
    /// <param name="options">Optional render options. Uses <see cref="RenderOptions.Default"/> when null.</param>
    public void Convert(string input, Stream output, RenderOptions? options = null)
    {
        var opts = options ?? RenderOptions.Default;

        if (!_enableCaching)
        {
            ConvertUncached(input, output, opts);
            return;
        }

        EnsureCaches();
        var inputHash = CacheKeyBuilder.ComputeInputHash(input);

        // Check render cache first (skips parse + extensions + render)
        var renderKey = CacheKeyBuilder.ComputeRenderKey(inputHash, Renderer.Format, opts);
        if (_renderCache!.TryGet(renderKey, out var cachedBytes))
        {
            var finalCached = RunOutputProcessors(cachedBytes);
            output.Write(finalCached, 0, finalCached.Length);
            LastExtensionDiagnostics = Array.Empty<Diagnostic>();
            return;
        }

        // Check parse cache (skips parse only)
        if (!_parseCache!.TryGet(inputHash, out var doc))
        {
            doc = Parser(input);
            _parseCache.Set(inputHash, doc);
        }

        RunExtensions(doc, opts);

        // Render to buffer, cache pre-processor output, run processors, write
        using var buffer = new MemoryStream();
        Renderer.Render(doc, buffer, opts);
        var bytes = buffer.ToArray();
        _renderCache.Set(renderKey, bytes);
        var final = RunOutputProcessors(bytes);
        output.Write(final, 0, final.Length);
    }

    /// <summary>
    /// Reads an AsciiDoc file from disk, parses it, and writes the rendered output to <paramref name="output"/>.
    /// </summary>
    /// <param name="inputPath">The path to the AsciiDoc source file.</param>
    /// <param name="output">The stream to write the rendered output to.</param>
    /// <param name="options">Optional render options. Uses <see cref="RenderOptions.Default"/> when null.</param>
    public void ConvertFile(string inputPath, Stream output, RenderOptions? options = null)
    {
        var text = File.ReadAllText(inputPath);
        Convert(text, output, options);
    }

    /// <summary>
    /// Returns metadata for all installed extensions from the registry.
    /// Does not load or register any extensions — read-only query.
    /// </summary>
    /// <param name="extensionsDir">Custom registry directory, or null for default (~/.adocnet/).</param>
    /// <param name="onWarning">Optional callback for non-fatal warnings.</param>
    /// <returns>List of installed extension metadata, sorted by name.</returns>
    public static IReadOnlyList<ExtensionInfo> GetInstalledExtensions(
        string? extensionsDir = null,
        Action<string>? onWarning = null)
    {
        var registry = ExtensionRegistry.Load(extensionsDir, onWarning);
        return registry.GetAll();
    }

    /// <summary>
    /// Finds a specific installed extension by name from the registry.
    /// Does not load or register the extension — read-only query.
    /// </summary>
    /// <param name="name">Extension name to find.</param>
    /// <param name="extensionsDir">Custom registry directory, or null for default (~/.adocnet/).</param>
    /// <param name="onWarning">Optional callback for non-fatal warnings.</param>
    /// <returns>Extension info if found, null otherwise.</returns>
    public static ExtensionInfo? FindExtension(
        string name,
        string? extensionsDir = null,
        Action<string>? onWarning = null)
    {
        var registry = ExtensionRegistry.Load(extensionsDir, onWarning);
        return registry.Find(name);
    }

    /// <summary>
    /// Loads extensions from a single assembly file, returning structured results.
    /// Successfully loaded processors are still registered into the engine.
    /// Must be called before the first <see cref="Convert"/> call.
    /// </summary>
    /// <param name="assemblyPath">Path to the extension assembly DLL.</param>
    /// <returns>Structured load results for each extension found.</returns>
    public IReadOnlyList<ExtensionLoadResult> LoadExtensionSafe(string assemblyPath)
    {
        ThrowIfFrozen();
        var warnings = new List<string>();
        var extensions = ExtensionLoader.LoadAssembly(assemblyPath, msg => warnings.Add(msg));
        var results = BuildLoadResults(assemblyPath, extensions, warnings);
        RegisterExtensions(extensions);
        return results;
    }

    /// <summary>
    /// Loads extensions from all DLLs in a directory, returning structured results.
    /// Successfully loaded processors are still registered into the engine.
    /// Must be called before the first <see cref="Convert"/> call.
    /// </summary>
    /// <param name="directoryPath">Path to the directory containing extension DLLs.</param>
    /// <returns>Structured load results for each extension found.</returns>
    public IReadOnlyList<ExtensionLoadResult> LoadExtensionsSafe(string directoryPath)
    {
        ThrowIfFrozen();
        var warnings = new List<string>();
        var extensions = ExtensionLoader.LoadDirectory(directoryPath, msg => warnings.Add(msg));
        var results = BuildLoadResults(directoryPath, extensions, warnings);
        RegisterExtensions(extensions);
        return results;
    }

    private static List<ExtensionLoadResult> BuildLoadResults(
        string source, List<object> extensions, List<string> warnings)
    {
        var results = new List<ExtensionLoadResult>();

        if (extensions.Count > 0)
        {
            // Group processors by name (IExtension.Name or type assembly)
            var name = ResolveExtensionName(extensions, source);
            results.Add(new ExtensionLoadResult(name, ExtensionState.Loaded, null, extensions));
        }

        if (warnings.Count > 0 && extensions.Count == 0)
        {
            // All warnings, no processors — treat as a single failed load
            var name = Path.GetFileNameWithoutExtension(source);
            var reason = string.Join("; ", warnings);
            results.Add(new ExtensionLoadResult(name, ExtensionState.Failed, reason, null));
        }

        return results;
    }

    private static string ResolveExtensionName(List<object> extensions, string source)
    {
        foreach (var ext in extensions)
        {
            if (ext is IExtension meta && !string.IsNullOrWhiteSpace(meta.Name))
                return meta.Name;
        }
        return Path.GetFileNameWithoutExtension(source);
    }

    /// <summary>
    /// Parses a snapshot's text using the cache-assisted incremental approach.
    /// If caching is enabled and the text matches a cached parse, returns the cached result.
    /// Otherwise performs a full re-parse. Returns a new snapshot with the parse result populated.
    /// </summary>
    /// <param name="snapshot">The snapshot containing the text to parse.</param>
    /// <returns>A new snapshot with <see cref="DocumentSnapshot.Document"/> populated.</returns>
    public DocumentSnapshot ParseIncremental(DocumentSnapshot snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

        if (_enableCaching)
        {
            EnsureCaches();
            var inputHash = CacheKeyBuilder.ComputeInputHash(snapshot.Text);

            if (_parseCache!.TryGet(inputHash, out var cachedDoc))
                return new DocumentSnapshot(snapshot.Version, snapshot.Text, cachedDoc);

            var doc = Parser(snapshot.Text);
            _parseCache.Set(inputHash, doc);
            return new DocumentSnapshot(snapshot.Version, snapshot.Text, doc);
        }

        var parsed = Parser(snapshot.Text);
        return new DocumentSnapshot(snapshot.Version, snapshot.Text, parsed);
    }

    /// <summary>
    /// Clears all cached parse results and render outputs.
    /// Call this if external state affecting extensions has changed.
    /// </summary>
    public void ClearCache()
    {
        _parseCache?.Clear();
        _renderCache?.Clear();
    }

    private void ClearCacheInternal()
    {
        _parseCache?.Clear();
        _renderCache?.Clear();
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

    private void RunExtensions(DocumentNode doc, RenderOptions opts)
    {
        if (_documentProcessors.Count > 0 || _blockProcessors.Count > 0 || _inlineProcessors.Count > 0)
        {
            _frozen = true;
            var context = new RenderContext(doc, opts);
            ProcessingPipeline.Run(doc, context, _documentProcessors, _blockProcessors, _inlineProcessors,
                OnWarning, _failureCounts, _disabledProcessors, MaxProcessorFailures);
            LastExtensionDiagnostics = context.Diagnostics;
        }
        else
        {
            LastExtensionDiagnostics = Array.Empty<Diagnostic>();
        }
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

    private void EnsureCaches()
    {
        _parseCache ??= new LruCache<string, DocumentNode>(_maxCacheEntries);
        _renderCache ??= new LruCache<string, byte[]>(_maxCacheEntries);
    }

    /// <summary>
    /// Calls <see cref="IExtensionLifecycle.Dispose"/> on all loaded extensions that
    /// implement the lifecycle interface. Call when the engine is no longer needed.
    /// </summary>
    public void Shutdown()
    {
        foreach (var lifecycle in _lifecycleExtensions)
        {
            try
            {
                lifecycle.Dispose();
            }
            catch (Exception ex)
            {
                OnWarning?.Invoke($"Extension lifecycle Dispose failed: {ex.Message}");
            }
        }
    }

    private void RegisterExtensions(List<object> extensions)
    {
        foreach (var instance in extensions)
        {
            if (instance is IDocumentProcessor dp)
                _documentProcessors.Add(dp);
            if (instance is IBlockProcessor bp)
                _blockProcessors.Add(bp);
            if (instance is IInlineProcessor ip)
                _inlineProcessors.Add(ip);
            if (instance is IOutputProcessor op)
                _outputProcessors.Add(op);

            if (instance is IExtensionLifecycle lifecycle)
            {
                try
                {
                    lifecycle.Initialize();
                    _lifecycleExtensions.Add(lifecycle);
                }
                catch (Exception ex)
                {
                    OnWarning?.Invoke($"Extension lifecycle Initialize failed: {ex.Message}");
                }
            }
        }
    }

    private void ThrowIfFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException("Cannot register processors after the first Convert() call.");
    }
}
