using AdocNet.Ast;

namespace AdocNet;

/// <summary>
/// A custom rendering template for specific AST nodes. Templates are checked
/// before the built-in renderer — the first template whose <see cref="CanRender"/>
/// returns true produces the output for that node.
/// </summary>
public interface INodeTemplate
{
    /// <summary>
    /// Returns true if this template can render the given node.
    /// </summary>
    bool CanRender(AstNode node);

    /// <summary>
    /// Renders the node to an HTML string.
    /// </summary>
    string Render(AstNode node, RenderContext context);
}
