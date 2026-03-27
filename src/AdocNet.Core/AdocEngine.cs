using AdocNet.Ast;
using AdocNet.Extensions;

namespace AdocNet;

/// <summary>
/// High-level facade that combines parsing and rendering of AsciiDoc source text.
/// </summary>
public sealed class AdocEngine
{
    private readonly List<IDocumentProcessor> _documentProcessors = new();
    private readonly List<IBlockProcessor> _blockProcessors = new();
    private readonly List<IInlineProcessor> _inlineProcessors = new();
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
            ProcessingPipeline.Run(doc, context, _documentProcessors, _blockProcessors, _inlineProcessors, OnWarning);
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
