using AdocNet.Ast;

namespace AdocNet;

/// <summary>
/// Per-render state passed to renderers, providing access to the document, options, and per-render shared state.
/// </summary>
public sealed class RenderContext
{
    private readonly Dictionary<Type, object> _state = new();
    private readonly List<Diagnostic> _diagnostics = new();

    /// <summary>Gets the document being rendered.</summary>
    public DocumentNode Document { get; }

    /// <summary>Gets the render options for the current render operation.</summary>
    public RenderOptions Options { get; }

    /// <summary>Gets the document-level attributes.</summary>
    public IReadOnlyDictionary<string, string> Attributes => Document.Attributes;

    /// <summary>
    /// Gets all diagnostics emitted by extensions during processing.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;

    /// <summary>
    /// Initializes a new <see cref="RenderContext"/> for the given document and options.
    /// </summary>
    /// <param name="document">The document AST to render.</param>
    /// <param name="options">The render options.</param>
    public RenderContext(DocumentNode document, RenderOptions options)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// Adds a diagnostic produced during extension processing.
    /// </summary>
    /// <param name="diagnostic">The diagnostic to add.</param>
    public void AddDiagnostic(Diagnostic diagnostic)
    {
        _diagnostics.Add(diagnostic ?? throw new ArgumentNullException(nameof(diagnostic)));
    }

    /// <summary>
    /// Gets or creates a shared state object of type <typeparamref name="T"/> for the duration of the render.
    /// </summary>
    /// <typeparam name="T">The type of state object.</typeparam>
    /// <param name="factory">A factory function invoked if the state does not yet exist.</param>
    /// <returns>The existing or newly created state object.</returns>
    public T GetOrCreate<T>(Func<T> factory) where T : class
    {
        var key = typeof(T);
        if (!_state.TryGetValue(key, out var value))
        {
            value = factory();
            _state[key] = value;
        }
        return (T)value;
    }
}
