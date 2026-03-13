using AdocNet.Ast;

namespace AdocNet;

public sealed class RenderContext
{
    private readonly Dictionary<Type, object> _state = new();

    public DocumentNode Document { get; }
    public RenderOptions Options { get; }
    public IReadOnlyDictionary<string, string> Attributes => Document.Attributes;

    public RenderContext(DocumentNode document, RenderOptions options)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

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
