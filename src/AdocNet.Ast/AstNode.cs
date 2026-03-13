using AdocNet;
using AdocNet.Internal.Compatibility;

namespace AdocNet.Ast;

/// <summary>
/// Base class for all AST nodes.
/// </summary>
public abstract class AstNode
{
    private readonly List<AstNode> _children = [];

    public SourceRange Source { get; set; }

    public IReadOnlyList<AstNode> Children => _children;

    public abstract AstNodeKind Kind { get; }

    public void AddChild(AstNode child)
    {
        Guard.NotNull(child);
        _children.Add(child);
    }

    public void InsertChild(int index, AstNode child)
    {
        Guard.NotNull(child);
        _children.Insert(index, child);
    }

    /// <summary>
    /// Returns node-specific properties for pretty-printing.
    /// Override in subclasses that have meaningful properties beyond children.
    /// </summary>
    public virtual IEnumerable<KeyValuePair<string, string>> GetProperties() => [];
}
