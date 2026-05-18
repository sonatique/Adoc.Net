using AdocNet.Ast;

namespace AdocNet.Emitter;

/// <summary>
/// Emits the <c>[[id]]</c> anchor and <c>[role,options="..."]</c> attribute
/// lines that precede a block, when the corresponding properties are set on a
/// <see cref="BlockNode"/>. Block title is handled separately by each emitter
/// because not every block stores its title the same way.
/// </summary>
internal static class BlockAttributesEmitter
{
    public static void Emit(AstNode node, EmitContext ctx)
    {
        if (node is not BlockNode block) return;

        bool hasId = !string.IsNullOrEmpty(block.Id);
        bool hasRoles = block.Roles.Count > 0;

        if (hasId && !hasRoles)
        {
            // Prefer the [[id]] form when there's only an id and no roles.
            ctx.Output.Append("[[");
            ctx.Output.Append(block.Id);
            if (!string.IsNullOrEmpty(block.Reftext))
            {
                ctx.Output.Append(',');
                ctx.Output.Append(block.Reftext);
            }
            ctx.Output.Append("]]\n");
            return;
        }

        if (hasId || hasRoles)
        {
            // Shorthand [#id.role1.role2] form when there's no reftext.
            if (string.IsNullOrEmpty(block.Reftext))
            {
                ctx.Output.Append('[');
                if (hasId)
                {
                    ctx.Output.Append('#');
                    ctx.Output.Append(block.Id);
                }
                foreach (var role in block.Roles)
                {
                    ctx.Output.Append('.');
                    ctx.Output.Append(role);
                }
                ctx.Output.Append("]\n");
            }
            else
            {
                // Long form when reftext is present (shorthand can't carry reftext).
                ctx.Output.Append('[');
                bool first = true;
                if (hasId)
                {
                    ctx.Output.Append("id=\"");
                    ctx.Output.Append(block.Id);
                    ctx.Output.Append('"');
                    first = false;
                }
                if (!string.IsNullOrEmpty(block.Reftext))
                {
                    if (!first) ctx.Output.Append(", ");
                    ctx.Output.Append("reftext=\"");
                    ctx.Output.Append(block.Reftext);
                    ctx.Output.Append('"');
                    first = false;
                }
                if (hasRoles)
                {
                    if (!first) ctx.Output.Append(", ");
                    ctx.Output.Append("role=\"");
                    ctx.Output.Append(string.Join(" ", block.Roles));
                    ctx.Output.Append('"');
                }
                ctx.Output.Append("]\n");
            }
        }
    }
}
