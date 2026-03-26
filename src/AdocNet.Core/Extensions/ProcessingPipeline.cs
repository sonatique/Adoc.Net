using AdocNet.Ast;

namespace AdocNet.Extensions;

/// <summary>
/// Executes registered document, block, and inline processors against an AST.
/// The pipeline walks the tree depth-first, running processors in FIFO registration order.
/// </summary>
internal static class ProcessingPipeline
{
    /// <summary>
    /// Runs all registered processors against the document AST.
    /// Execution order: document processors, then block processors, then inline processors.
    /// </summary>
    internal static void Run(
        DocumentNode document,
        RenderContext context,
        IReadOnlyList<IDocumentProcessor> documentProcessors,
        IReadOnlyList<IBlockProcessor> blockProcessors,
        IReadOnlyList<IInlineProcessor> inlineProcessors,
        Action<string>? onWarning)
    {
        // Phase 1: Document processors (FIFO)
        foreach (var processor in documentProcessors)
        {
            try
            {
                processor.Process(document);
            }
            catch (Exception ex)
            {
                onWarning?.Invoke(
                    $"Processor {processor.GetType().Name} threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        // Phase 2: Block processors (depth-first walk)
        if (blockProcessors.Count > 0)
            WalkBlocks(document, blockProcessors, context, onWarning);

        // Phase 3: Inline processors (depth-first walk)
        if (inlineProcessors.Count > 0)
            WalkAllInlines(document, inlineProcessors, context, onWarning);
    }

    // ── Block walk ───────────────────────────────────────────────────────

    private static void WalkBlocks(
        AstNode parent,
        IReadOnlyList<IBlockProcessor> processors,
        RenderContext context,
        Action<string>? onWarning)
    {
        for (int i = 0; i < parent.Children.Count; i++)
        {
            if (parent.Children[i] is not BlockNode block)
                continue;

            // Run all processors on this block (FIFO)
            foreach (var processor in processors)
            {
                try
                {
                    if (processor.CanProcess(block))
                        processor.Process(block, context);
                }
                catch (Exception ex)
                {
                    onWarning?.Invoke(
                        $"Processor {processor.GetType().Name} threw {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Apply pending replacements
            var replacements = context.GetOrCreate(() => new NodeReplacements());
            if (replacements.HasPending)
            {
                ApplyReplacements(parent, replacements);
                replacements.Clear();
                // Re-check current index after mutation
                i--;
                continue;
            }

            // Recurse into the block's children
            WalkBlocks(block, processors, context, onWarning);
        }
    }

    // ── Inline walk ──────────────────────────────────────────────────────

    private static void WalkAllInlines(
        AstNode parent,
        IReadOnlyList<IInlineProcessor> processors,
        RenderContext context,
        Action<string>? onWarning)
    {
        // Walk block nodes in Children to find inline containers
        for (int i = 0; i < parent.Children.Count; i++)
        {
            var child = parent.Children[i];

            // Extract inline lists from block nodes that contain them
            if (child is BlockNode)
            {
                ProcessInlineLists(child, processors, context, onWarning);
            }

            // Recurse into children (sections contain blocks, blocks may nest)
            WalkAllInlines(child, processors, context, onWarning);
        }
    }

    private static void ProcessInlineLists(
        AstNode node,
        IReadOnlyList<IInlineProcessor> processors,
        RenderContext context,
        Action<string>? onWarning)
    {
        // Each block type stores inlines in different properties.
        // We must enumerate them explicitly since they are not in AstNode.Children.
        switch (node)
        {
            case ParagraphNode p:
                WalkInlineList(p.Inlines, processors, context, onWarning);
                break;
            case ListItemNode li:
                WalkInlineList(li.Inlines, processors, context, onWarning);
                break;
            case AdmonitionNode a:
                WalkInlineList(a.Inlines, processors, context, onWarning);
                break;
            case SectionNode s:
                WalkInlineList(s.TitleInlines, processors, context, onWarning);
                break;
            case TableCellNode tc:
                WalkInlineList(tc.Inlines, processors, context, onWarning);
                break;
            case DescriptionItemNode di:
                WalkInlineList(di.TermInlines, processors, context, onWarning);
                WalkInlineList(di.DescriptionInlines, processors, context, onWarning);
                break;
            case BibliographyEntryNode be:
                WalkInlineList(be.Inlines, processors, context, onWarning);
                break;
        }
    }

    private static void WalkInlineList(
        IReadOnlyList<InlineNode> inlines,
        IReadOnlyList<IInlineProcessor> processors,
        RenderContext context,
        Action<string>? onWarning)
    {
        for (int i = 0; i < inlines.Count; i++)
        {
            var inline = inlines[i];

            // Run all processors on this inline (FIFO)
            foreach (var processor in processors)
            {
                try
                {
                    if (processor.CanProcess(inline))
                        processor.Process(inline, context);
                }
                catch (Exception ex)
                {
                    onWarning?.Invoke(
                        $"Processor {processor.GetType().Name} threw {ex.GetType().Name}: {ex.Message}");
                }
            }

            // Apply pending replacements on this inline list
            var replacements = context.GetOrCreate(() => new NodeReplacements());
            if (replacements.HasPending)
            {
                ApplyInlineReplacements(inlines, replacements);
                replacements.Clear();
                i--;
                continue;
            }

            // Recurse into inline containers that have their own children
            switch (inline)
            {
                case StrongInlineNode strong:
                    WalkInlineList(strong.Children, processors, context, onWarning);
                    break;
                case EmphasisInlineNode emphasis:
                    WalkInlineList(emphasis.Children, processors, context, onWarning);
                    break;
                case MonospaceInlineNode mono:
                    WalkInlineList(mono.Children, processors, context, onWarning);
                    break;
                case HighlightInlineNode highlight:
                    WalkInlineList(highlight.Children, processors, context, onWarning);
                    break;
                case FootnoteInlineNode footnote:
                    WalkInlineList(footnote.Inlines, processors, context, onWarning);
                    break;
            }
        }
    }

    // ── Replacement application ──────────────────────────────────────────

    private static void ApplyReplacements(AstNode parent, NodeReplacements replacements)
    {
        // Walk children backwards to avoid index shifting issues
        var children = (List<AstNode>)parent.Children;
        for (int i = children.Count - 1; i >= 0; i--)
        {
            if (!replacements.TryGet(children[i], out var replacement))
                continue;

            if (replacement is null)
                children.RemoveAt(i);
            else
                children[i] = replacement;
        }
    }

    private static void ApplyInlineReplacements(
        IReadOnlyList<InlineNode> inlines, NodeReplacements replacements)
    {
        // Cast to IList to mutate — backed by List<InlineNode> (parser) or InlineNode[] (tests)
        var list = (IList<InlineNode>)inlines;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (!replacements.TryGet(list[i], out var replacement))
                continue;

            if (replacement is null)
                list.RemoveAt(i);
            else
                list[i] = (InlineNode)replacement;
        }
    }
}
