using global::Avalonia;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Primitives;
using global::Avalonia.Media;
using AdocNet.Ast;
using AdocNet.Editor;
using AdocNet.Layout;
using AdocNet.Layout.Builders;

namespace AdocNet.Avalonia;

/// <summary>
/// Avalonia-side analogue of <see cref="AdocNet.Editor.IncrementalHtmlRenderer"/>:
/// re-renders only the top-level AST children that changed between two parses,
/// splicing the new Avalonia control subtrees into the existing visual
/// tree in place. Used by the hybrid editor to keep preview updates cheap
/// on large documents.
///
/// <para>Mechanism:</para>
/// <list type="number">
///   <item><description><b>Initial render</b>: render one <b>container</b>
///     (a <see cref="StackPanel"/>) per top-level <see cref="AstNode.Children"/>
///     entry, tagged with that AST node's index +
///     <see cref="AstNode.StructuralHash"/> via <see cref="Control.Tag"/>.
///     A container holds <em>all</em> the layout blocks a single AST node
///     expands to — e.g. a <see cref="SectionNode"/> expands to a heading
///     plus its body blocks, all inside one container.</description></item>
///   <item><description><b>Subsequent renders</b>: run
///     <see cref="AstDiffer.DiffSections"/> to identify Modified children;
///     for each one, rebuild only that child's container and replace it.
///     Added / Removed / metadata changes fall back to a full
///     re-render.</description></item>
/// </list>
///
/// <para><b>Why one container per AST child (not one panel child per layout
/// block):</b> <see cref="LayoutBuilder"/> flattens a section into a heading
/// followed by its body blocks, so the number of layout/visual blocks does
/// <em>not</em> match the number of AST children once any section is present.
/// Grouping each AST node's blocks into a single tagged container keeps a
/// 1:1 mapping between <see cref="AstNode.Children"/> and the
/// containers, which is what both the section diff and any block-level
/// editor interaction rely on.</para>
/// </summary>
public sealed class IncrementalAvaloniaRenderer
{
    private readonly AvaloniaRenderer _renderer;
    private readonly LayoutBuilder _layoutBuilder = new();

    public IncrementalAvaloniaRenderer() : this(new AvaloniaRenderer()) { }

    public IncrementalAvaloniaRenderer(AvaloniaRenderer renderer)
    {
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    /// <summary>
    /// Full render. The returned control's top-level containers are tagged
    /// so a subsequent <see cref="RenderIncremental"/> call can diff against
    /// this document.
    /// </summary>
    public Control Render(DocumentNode document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));

        var panel = new StackPanel { Margin = new Thickness(16) };

        // The document title (`= Title`) is metadata, not an AST child, so it
        // gets an untagged leading block. Any title change forces a full
        // re-render (see HasMetadataChanged), so its presence is stable
        // across incremental updates and containers are always located by
        // tag rather than by absolute position.
        if (!string.IsNullOrEmpty(document.Title))
            panel.Children.Add(CreateTitleBlock(document.Title!));

        for (int i = 0; i < document.Children.Count; i++)
        {
            var container = BuildChildContainer(document.Children[i]);
            TagControl(container, i, document.Children[i].StructuralHash);
            panel.Children.Add(container);
        }

        return _renderer.WrapInScrollViewer
            ? new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
            : panel;
    }

    /// <summary>
    /// Attempts an incremental update of <paramref name="previousControl"/>
    /// from <paramref name="oldDoc"/> to <paramref name="newDoc"/>. When
    /// incremental is possible the existing control is mutated in place
    /// and returned; otherwise a full re-render produces a fresh control.
    /// </summary>
    public Control RenderIncremental(DocumentNode oldDoc, DocumentNode newDoc, Control previousControl)
    {
        if (oldDoc is null) throw new ArgumentNullException(nameof(oldDoc));
        if (newDoc is null) throw new ArgumentNullException(nameof(newDoc));
        if (previousControl is null) throw new ArgumentNullException(nameof(previousControl));

        // Metadata mismatch (title or attribute set) forces a full re-render
        // — both can affect numbering, generated TOC entries, or other
        // section-spanning state.
        if (HasMetadataChanged(oldDoc, newDoc))
            return Render(newDoc);

        var diff = AstDiffer.DiffSections(oldDoc, newDoc);

        // Structural changes (insert / delete) shift child indices, which
        // would invalidate the tagged containers. Cheaper and safer to
        // start over.
        if (HasStructuralChange(diff))
            return Render(newDoc);

        // No changes at all → nothing to do.
        if (AllUnchanged(diff))
            return previousControl;

        var panel = ExtractTopPanel(previousControl);
        if (panel is null)
            return Render(newDoc);

        // For every Modified AST child, rebuild that child's container and
        // replace it in place. Containers are matched by their tagged AST
        // index (not absolute panel position) so an optional leading title
        // block doesn't shift the mapping.
        foreach (var entry in diff)
        {
            if (entry.ChangeType != AstDiffChangeType.Modified) continue;
            if (entry.NewNode is null) continue;

            int panelIndex = FindContainerIndex(panel, entry.Index);
            if (panelIndex < 0) continue;

            var newContainer = BuildChildContainer(entry.NewNode);
            TagControl(newContainer, entry.Index, entry.NewNode.StructuralHash);
            panel.Children[panelIndex] = newContainer;
        }
        return previousControl;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a single top-level AST child into one container control that
    /// holds <em>every</em> layout block the node expands to (a section's
    /// heading plus all its body blocks, a paragraph's single block, etc.).
    /// The container is always returned even when empty so the 1:1 mapping
    /// between AST children and containers is preserved.
    /// </summary>
    private StackPanel BuildChildContainer(AstNode node)
    {
        // LayoutBuilder works on a DocumentNode; build a temporary one that
        // holds only this child so we can reuse the same public API. A
        // section flattens to [heading, ...body]; we keep them together.
        var temp = new DocumentNode();
        temp.AddChild(node);

        var layout = _layoutBuilder.Build(temp);

        // No outer margin/padding: the inner blocks already carry their own
        // spacing, so a bare vertical StackPanel stacks them identically to
        // the flat full-document render.
        var container = new StackPanel();
        foreach (var block in layout.Children)
        {
            var control = _renderer.Render(block);
            if (control != null)
                container.Children.Add(control);
        }
        return container;
    }

    /// <summary>
    /// Builds the leading document-title block, mirroring the styling used
    /// by <see cref="AvaloniaRenderer.Render(DocumentLayout)"/> so the
    /// incremental output is visually identical to a full render.
    /// </summary>
    private static TextBlock CreateTitleBlock(string title) => new()
    {
        Text = title,
        FontSize = 28,
        FontWeight = FontWeight.Bold,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 12),
    };

    private static void TagControl(Control c, int index, int hash)
    {
        c.Tag = new SectionTag(index, hash);
    }

    /// <summary>
    /// Returns the index within <paramref name="panel"/> of the container
    /// tagged with the given AST child index, or -1 if none matches.
    /// </summary>
    private static int FindContainerIndex(StackPanel panel, int astIndex)
    {
        for (int i = 0; i < panel.Children.Count; i++)
        {
            if (panel.Children[i] is Control c && c.Tag is SectionTag tag && tag.Index == astIndex)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Walks the rendered control returned by <see cref="Render(DocumentNode)"/>
    /// to find the top-level <see cref="StackPanel"/> that holds the tagged
    /// containers. The render wraps the panel in a <see cref="ScrollViewer"/>
    /// when <see cref="AvaloniaRenderer.WrapInScrollViewer"/> is set, so this
    /// peels that layer.
    /// </summary>
    private static StackPanel? ExtractTopPanel(Control rendered) => rendered switch
    {
        ScrollViewer sv when sv.Content is StackPanel sp => sp,
        StackPanel sp                                    => sp,
        _                                                => null,
    };

    private static bool HasMetadataChanged(DocumentNode oldDoc, DocumentNode newDoc)
    {
        if (!string.Equals(oldDoc.Title, newDoc.Title, StringComparison.Ordinal))
            return true;
        if (oldDoc.Attributes.Count != newDoc.Attributes.Count)
            return true;
        foreach (var kvp in newDoc.Attributes)
        {
            if (!oldDoc.Attributes.TryGetValue(kvp.Key, out var oldVal)) return true;
            if (!string.Equals(oldVal, kvp.Value, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static bool HasStructuralChange(IReadOnlyList<AstDiffEntry> diff)
    {
        foreach (var e in diff)
        {
            if (e.ChangeType == AstDiffChangeType.Added) return true;
            if (e.ChangeType == AstDiffChangeType.Removed) return true;
        }
        return false;
    }

    private static bool AllUnchanged(IReadOnlyList<AstDiffEntry> diff)
    {
        foreach (var e in diff)
        {
            if (e.ChangeType != AstDiffChangeType.Unchanged) return false;
        }
        return true;
    }

    /// <summary>
    /// Tag payload attached to top-level containers. Carries both the AST
    /// child index (positional matching) and the structural hash (equality
    /// check for diagnostics / future invalidation work).
    /// </summary>
    public readonly record struct SectionTag(int Index, int StructuralHash);
}
