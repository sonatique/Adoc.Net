using System.IO;
using System.Text;
using AdocNet.Ast;

namespace AdocNet.Emitter;

/// <summary>
/// Serialises an Adoc.Net AST back to AsciiDoc source. Two modes:
/// <list type="bullet">
///   <item>
///     <description>
///       <b>From-AST synthesis</b> (default): walks the AST and produces
///       AsciiDoc source from the typed node properties. Surface-form
///       choices for ambiguous nodes are controlled by <see cref="EmitOptions"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Source-anchored</b> (when <see cref="EmitOptions.PreserveOriginalWhenAvailable"/>
///       is enabled and <see cref="EmitOptions.OriginalSource"/> is supplied):
///       any node carrying a populated <c>SourceRange</c> emits its original
///       source slice byte-identical, falling back to synthesis only for
///       AST nodes with no source range (e.g. synthetic mutations).
///     </description>
///   </item>
/// </list>
/// The correctness criterion for the from-AST path is round-trip equality:
/// <c>parse(emit(parse(x))).StructuralHash == parse(x).StructuralHash</c>.
/// The source-anchored path additionally guarantees byte-identical output
/// for any AST produced directly by parsing an unmodified document.
/// </summary>
public sealed class AsciidocEmitter
{
    /// <summary>
    /// Emits <paramref name="node"/> to a string using the given options.
    /// </summary>
    public string Emit(AstNode node, EmitOptions? options = null)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));

        var effective = options ?? EmitOptions.Default;

        // Root-level shortcut for the source-anchored path: the parser does
        // not populate <c>Source</c> on <see cref="DocumentNode"/> itself
        // (only on its children), so a per-node source-anchored emit would
        // dispatch the root through synthesis even when the original source
        // is right there. When the caller has supplied the original source,
        // returning it verbatim for the root delivers the byte-identical
        // guarantee that source-anchored emit promises for any unmodified
        // AST produced directly by parsing.
        if (node is DocumentNode
            && effective.PreserveOriginalWhenAvailable
            && effective.OriginalSource is not null)
        {
            return effective.OriginalSource;
        }

        var sb = new StringBuilder();
        var ctx = new EmitContext(sb, effective);
        EmitNode(node, ctx);
        return sb.ToString();
    }

    /// <summary>
    /// Emits <paramref name="node"/> to <paramref name="writer"/>.
    /// </summary>
    public void Emit(AstNode node, TextWriter writer, EmitOptions? options = null)
    {
        if (node is null) throw new ArgumentNullException(nameof(node));
        if (writer is null) throw new ArgumentNullException(nameof(writer));

        writer.Write(Emit(node, options));
    }

    // ── Dispatch ────────────────────────────────────────────────────────────

    internal static void EmitNode(AstNode node, EmitContext ctx)
    {
        // Source-anchored fast path applies uniformly to every node type.
        // Synthesis dispatch is only used when the fast path is unavailable
        // or returns false.
        if (ctx.TryEmitOriginal(node)) return;

        switch (node)
        {
            case DocumentNode doc:
                DocumentEmitter.Emit(doc, ctx);
                break;
            case SectionNode section:
                SectionEmitter.Emit(section, ctx);
                break;
            case ParagraphNode paragraph:
                ParagraphEmitter.Emit(paragraph, ctx);
                break;
            case ThematicBreakNode thematicBreak:
                ThematicBreakEmitter.Emit(thematicBreak, ctx);
                break;
            case PageBreakNode pageBreak:
                PageBreakEmitter.Emit(pageBreak, ctx);
                break;
            default:
                // TODO: extend with the remaining node kinds. For now,
                // unhandled nodes are emitted as a single-line marker so
                // round-trip failures surface clearly during development.
                ctx.Output.Append("// [emitter: unhandled ");
                ctx.Output.Append(node.Kind);
                ctx.Output.Append("]\n");
                break;
        }
    }
}
