using AdocNet;
using AdocNet.Ast;

namespace CustomRenderer;

/// <summary>
/// A sample renderer that converts an AsciiDoc AST to Markdown.
/// Demonstrates how to extend <see cref="DocumentRendererBase"/>.
/// </summary>
public sealed class MarkdownRenderer : DocumentRendererBase
{
    public override string Format => "markdown";

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Retrieves the <see cref="StreamWriter"/> stored in the render context.
    /// </summary>
    private static StreamWriter GetWriter(RenderContext context)
        => context.GetOrCreate<StreamWriter>(() => throw new InvalidOperationException("Writer not initialized"));

    // ── Document ─────────────────────────────────────────────────────────────

    protected override void RenderDocument(RenderContext context, Stream output)
    {
        var writer = new StreamWriter(output, leaveOpen: true);
        // Store the writer in context so all Render* methods can retrieve it.
        context.GetOrCreate(() => writer);

        // Render document title as a top-level heading if present.
        if (context.Document.Title is not null)
        {
            writer.Write("# ");
            writer.WriteLine(context.Document.Title);
            writer.WriteLine();
        }

        RenderBlocks(context.Document.Children.OfType<BlockNode>(), context);
        writer.Flush();
    }

    // ── Block nodes ──────────────────────────────────────────────────────────

    protected override void RenderSection(SectionNode node, RenderContext context)
    {
        var writer = GetWriter(context);
        // Level 0 = document title (already handled), 1 = ##, 2 = ###, etc.
        int headingDepth = node.Level + 1;
        writer.Write(new string('#', headingDepth));
        writer.Write(' ');
        writer.WriteLine(node.Title);
        writer.WriteLine();

        RenderBlocks(node.Children.OfType<BlockNode>(), context);
    }

    protected override void RenderParagraph(ParagraphNode node, RenderContext context)
    {
        var writer = GetWriter(context);
        RenderInlines(node.Inlines, context);
        writer.WriteLine();
        writer.WriteLine();
    }

    protected override void RenderList(ListNode node, RenderContext context)
    {
        int index = 1;
        foreach (var child in node.Children.OfType<ListItemNode>())
        {
            var writer = GetWriter(context);
            if (node.ListKind == ListKind.Ordered)
                writer.Write($"{index++}. ");
            else
                writer.Write("- ");

            RenderInlines(child.Inlines, context);
            writer.WriteLine();

            // Render any nested content (e.g. nested lists).
            RenderBlocks(child.Children.OfType<BlockNode>(), context);
        }

        GetWriter(context).WriteLine();
    }

    protected override void RenderListItem(ListItemNode node, RenderContext context)
    {
        // List items are rendered inline by RenderList above.
        // This override prevents the base class from throwing.
        var writer = GetWriter(context);
        writer.Write("- ");
        RenderInlines(node.Inlines, context);
        writer.WriteLine();
    }

    protected override void RenderDelimitedBlock(DelimitedBlockNode node, RenderContext context)
    {
        var writer = GetWriter(context);

        switch (node.BlockKind)
        {
            case DelimitedBlockKind.Source:
            case DelimitedBlockKind.Listing:
                writer.Write("```");
                if (node.Language is not null)
                    writer.Write(node.Language);
                writer.WriteLine();
                writer.WriteLine(node.Content);
                writer.WriteLine("```");
                writer.WriteLine();
                break;

            case DelimitedBlockKind.Literal:
                writer.WriteLine("```");
                writer.WriteLine(node.Content);
                writer.WriteLine("```");
                writer.WriteLine();
                break;

            case DelimitedBlockKind.Quote:
                if (node.Content is not null)
                {
                    foreach (var line in node.Content.Split('\n'))
                    {
                        writer.Write("> ");
                        writer.WriteLine(line);
                    }
                }
                else
                {
                    // Structural quote block with parsed children.
                    foreach (var child in node.Children.OfType<BlockNode>())
                    {
                        writer.Write("> ");
                        RenderBlock(child, context);
                    }
                }
                writer.WriteLine();
                break;

            default:
                // Fallback: render content as-is or recurse into children.
                if (node.Content is not null)
                    writer.WriteLine(node.Content);
                else
                    RenderBlocks(node.Children.OfType<BlockNode>(), context);
                writer.WriteLine();
                break;
        }
    }

    // ── Inline nodes ─────────────────────────────────────────────────────────

    protected override void RenderTextInline(TextInlineNode node, RenderContext context)
    {
        GetWriter(context).Write(node.Value);
    }

    protected override void RenderStrongInline(StrongInlineNode node, RenderContext context)
    {
        var writer = GetWriter(context);
        writer.Write("**");
        RenderInlines(node.Children, context);
        writer.Write("**");
    }

    protected override void RenderEmphasisInline(EmphasisInlineNode node, RenderContext context)
    {
        var writer = GetWriter(context);
        writer.Write("*");
        RenderInlines(node.Children, context);
        writer.Write("*");
    }

    protected override void RenderMonospaceInline(MonospaceInlineNode node, RenderContext context)
    {
        var writer = GetWriter(context);
        writer.Write("`");
        RenderInlines(node.Children, context);
        writer.Write("`");
    }

    protected override void RenderLinkInline(LinkInlineNode node, RenderContext context)
    {
        var writer = GetWriter(context);
        writer.Write('[');
        writer.Write(node.Url);
        writer.Write("](");
        writer.Write(node.Url);
        writer.Write(')');
    }

    protected override void RenderInlineLinkMacro(InlineLinkMacroNode node, RenderContext context)
    {
        var writer = GetWriter(context);
        var label = string.IsNullOrEmpty(node.Label) ? node.Url : node.Label;
        writer.Write('[');
        writer.Write(label);
        writer.Write("](");
        writer.Write(node.Url);
        writer.Write(')');
    }
}
