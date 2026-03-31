using AdocNet.Ast;
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
    private readonly Dictionary<object, int> _failureCounts = new();
    private readonly HashSet<object> _disabledProcessors = new();
    private bool _frozen;

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
        var doc = Parser(input);
        var opts = options ?? RenderOptions.Default;

        if (_documentProcessors.Count > 0 || _blockProcessors.Count > 0 || _inlineProcessors.Count > 0)
        {
            _frozen = true;
            var context = new RenderContext(doc, opts);
            ProcessingPipeline.Run(doc, context, _documentProcessors, _blockProcessors, _inlineProcessors,
                OnWarning, _failureCounts, _disabledProcessors, MaxProcessorFailures);
        }

        Renderer.Render(doc, output, opts);
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
        }
    }

    private void ThrowIfFrozen()
    {
        if (_frozen)
            throw new InvalidOperationException("Cannot register processors after the first Convert() call.");
    }
}
