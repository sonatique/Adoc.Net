using AdocNet.Internal.Compatibility;

namespace AdocNet.Ast;

/// <summary>
/// The root node of an AsciiDoc document.
/// </summary>
public sealed class DocumentNode : AstNode
{
    private readonly Dictionary<string, string> _attributes = [];

    public override AstNodeKind Kind => AstNodeKind.Document;

    /// <summary>
    /// The document title from the <c>= Title</c> header line, or null if absent.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Document attribute entries parsed from <c>:name: value</c> header and body lines.
    /// </summary>
    public IReadOnlyDictionary<string, string> Attributes => _attributes;

    public void SetAttribute(string name, string value)
    {
        Guard.NotNullOrEmpty(name);
        _attributes[name] = value;
    }

    /// <summary>
    /// Removes a previously set attribute. Used during parsing for <c>:!name:</c> and <c>:name!:</c> unset syntax.
    /// </summary>
    public void RemoveAttribute(string name)
    {
        Guard.NotNullOrEmpty(name);
        _attributes.Remove(name);
    }

    public override IEnumerable<KeyValuePair<string, string>> GetProperties()
    {
        if (Title is not null)
            yield return new("Title", Title);
    }
}
