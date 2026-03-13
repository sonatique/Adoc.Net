using AdocNet.Ast;

namespace AdocNet;

public abstract class DocumentRendererBase : IDocumentRenderer
{
    public abstract string Format { get; }

    public void Render(DocumentNode document, Stream output, RenderOptions options)
    {
        var context = new RenderContext(document, options);
        RenderDocument(context, output);
    }

    protected abstract void RenderDocument(RenderContext context, Stream output);

    protected void RenderBlock(BlockNode node, RenderContext context)
    {
        switch (node)
        {
            case SectionNode n: RenderSection(n, context); break;
            case ParagraphNode n: RenderParagraph(n, context); break;
            case ListNode n: RenderList(n, context); break;
            case ListItemNode n: RenderListItem(n, context); break;
            case TableNode n: RenderTable(n, context); break;
            case DelimitedBlockNode n: RenderDelimitedBlock(n, context); break;
            case BlockImageNode n: RenderBlockImage(n, context); break;
            case AdmonitionNode n: RenderAdmonition(n, context); break;
            case DescriptionListNode n: RenderDescriptionList(n, context); break;
            case DescriptionItemNode n: RenderDescriptionItem(n, context); break;
            case ThematicBreakNode n: RenderThematicBreak(n, context); break;
            case PageBreakNode n: RenderPageBreak(n, context); break;
            case TocNode n: RenderToc(n, context); break;
            case VideoNode n: RenderVideo(n, context); break;
            case AudioNode n: RenderAudio(n, context); break;
            case IndexNode n: RenderIndex(n, context); break;
            case BibliographyEntryNode n: RenderBibliographyEntry(n, context); break;
            default: throw new NotSupportedException($"{Format} renderer does not support block node {node.GetType().Name}");
        }
    }

    protected void RenderInline(InlineNode node, RenderContext context)
    {
        switch (node)
        {
            case TextInlineNode n: RenderTextInline(n, context); break;
            case StrongInlineNode n: RenderStrongInline(n, context); break;
            case EmphasisInlineNode n: RenderEmphasisInline(n, context); break;
            case MonospaceInlineNode n: RenderMonospaceInline(n, context); break;
            case LinkInlineNode n: RenderLinkInline(n, context); break;
            case InlineLinkMacroNode n: RenderInlineLinkMacro(n, context); break;
            case InlineMacroNode n: RenderInlineMacro(n, context); break;
            case InlineImageNode n: RenderInlineImage(n, context); break;
            case FootnoteInlineNode n: RenderFootnoteInline(n, context); break;
            case CrossReferenceInlineNode n: RenderCrossReferenceInline(n, context); break;
            case InterDocumentXrefNode n: RenderInterDocumentXref(n, context); break;
            case PassthroughInlineNode n: RenderPassthroughInline(n, context); break;
            case SuperscriptInlineNode n: RenderSuperscriptInline(n, context); break;
            case SubscriptInlineNode n: RenderSubscriptInline(n, context); break;
            case HighlightInlineNode n: RenderHighlightInline(n, context); break;
            case IndexTermNode n: RenderIndexTerm(n, context); break;
            case IndexTermHiddenNode n: RenderIndexTermHidden(n, context); break;
            case InlineAnchorNode n: RenderInlineAnchor(n, context); break;
            default: throw new NotSupportedException($"{Format} renderer does not support inline node {node.GetType().Name}");
        }
    }

    protected void RenderBlocks(IEnumerable<BlockNode> nodes, RenderContext context)
    {
        foreach (var node in nodes)
            RenderBlock(node, context);
    }

    protected void RenderInlines(IEnumerable<InlineNode> nodes, RenderContext context)
    {
        foreach (var node in nodes)
            RenderInline(node, context);
    }

    // ── Block virtual methods ────────────────────────────────────────────

    protected virtual void RenderSection(SectionNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderParagraph(ParagraphNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderList(ListNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderListItem(ListItemNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderTable(TableNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderDelimitedBlock(DelimitedBlockNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderBlockImage(BlockImageNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderAdmonition(AdmonitionNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderDescriptionList(DescriptionListNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderDescriptionItem(DescriptionItemNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderThematicBreak(ThematicBreakNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderPageBreak(PageBreakNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderToc(TocNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderVideo(VideoNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderAudio(AudioNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderIndex(IndexNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderBibliographyEntry(BibliographyEntryNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    // ── Inline virtual methods ───────────────────────────────────────────

    protected virtual void RenderTextInline(TextInlineNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderStrongInline(StrongInlineNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderEmphasisInline(EmphasisInlineNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderMonospaceInline(MonospaceInlineNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderLinkInline(LinkInlineNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderInlineLinkMacro(InlineLinkMacroNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderInlineMacro(InlineMacroNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderInlineImage(InlineImageNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderFootnoteInline(FootnoteInlineNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderCrossReferenceInline(CrossReferenceInlineNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderInterDocumentXref(InterDocumentXrefNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderPassthroughInline(PassthroughInlineNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderSuperscriptInline(SuperscriptInlineNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderSubscriptInline(SubscriptInlineNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderHighlightInline(HighlightInlineNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderIndexTerm(IndexTermNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderIndexTermHidden(IndexTermHiddenNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");

    protected virtual void RenderInlineAnchor(InlineAnchorNode node, RenderContext context)
        => throw new NotSupportedException($"{Format} renderer does not support {node.Kind}");
}
