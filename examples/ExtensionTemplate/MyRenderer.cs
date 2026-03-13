using AdocNet;
using AdocNet.Ast;

namespace ExtensionTemplate;

/// <summary>
/// A minimal renderer skeleton. Copy this file as a starting point for your
/// own custom format. Override additional Render* methods as needed.
/// </summary>
public sealed class MyRenderer : DocumentRendererBase
{
    // TODO: Change this to your output format name (e.g. "latex", "plaintext", "json").
    public override string Format => "custom";

    private static StreamWriter GetWriter(RenderContext context)
        => context.GetOrCreate<StreamWriter>(() => throw new InvalidOperationException("Writer not initialized"));

    protected override void RenderDocument(RenderContext context, Stream output)
    {
        var writer = new StreamWriter(output, leaveOpen: true);
        context.GetOrCreate(() => writer);

        // TODO: Emit any document-level header or preamble here.
        if (context.Document.Title is not null)
            writer.WriteLine($"[DOCUMENT] {context.Document.Title}");

        foreach (var child in context.Document.Children.OfType<BlockNode>())
            RenderBlock(child, context);

        // TODO: Emit any document-level footer here.
        writer.Flush();
    }

    protected override void RenderSection(SectionNode node, RenderContext context)
    {
        var writer = GetWriter(context);
        // TODO: Replace with your format's section rendering.
        writer.WriteLine($"[SECTION L{node.Level}] {node.Title}");

        RenderBlocks(node.Children.OfType<BlockNode>(), context);
    }

    protected override void RenderParagraph(ParagraphNode node, RenderContext context)
    {
        var writer = GetWriter(context);
        // TODO: Replace with your format's paragraph rendering.
        // Use RenderInlines(node.Inlines, context) to process inline markup.
        writer.WriteLine($"[PARA] {node.Text}");
    }

    // TODO: Override more methods as needed:
    //   RenderList, RenderListItem, RenderDelimitedBlock,
    //   RenderTextInline, RenderStrongInline, RenderEmphasisInline, etc.
    //
    // See DocumentRendererBase for the full list of virtual methods.
    // See examples/CustomRenderer/ for a complete Markdown renderer.
}
