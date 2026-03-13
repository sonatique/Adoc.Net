using AdocNet;

namespace AdocNet.Ast;

/// <summary>
/// Base class for block-level AST nodes (sections, paragraphs, lists, delimited blocks, etc.).
/// </summary>
public abstract class BlockNode : AstNode
{
    /// <summary>Optional ID assigned via <c>[[id]]</c> block anchor or <c>[#id]</c> shorthand.</summary>
    public string? Id { get; set; }

    /// <summary>Optional roles assigned via <c>[.role]</c>, <c>[#id.role1.role2]</c>, or <c>[role="..."]</c> syntax.</summary>
    public IReadOnlyList<string> Roles { get; set; } = [];

    /// <summary>
    /// Substitution override from <c>[subs="..."]</c> block attribute.
    /// When null, the block uses its type's default substitution set.
    /// </summary>
    public SubstitutionKind? Substitutions { get; set; }
}
