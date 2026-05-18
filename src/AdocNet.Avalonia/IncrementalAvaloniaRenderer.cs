using global::Avalonia.Controls;
using AdocNet.Ast;
using AdocNet.Editor;
using AdocNet.Layout;
using AdocNet.Layout.Builders;

namespace AdocNet.Avalonia;

/// <summary>
/// Avalonia-side analogue of <see cref="AdocNet.Editor.IncrementalHtmlRenderer"/>:
/// re-renders only the top-level sections that changed between two parses,
/// splicing the new Avalonia control subtrees into the existing visual
/// tree in place. Used by the hybrid editor to keep preview updates cheap
/// on large documents.
///
/// <para>Mechanism:</para>
/// <list type="number">
///   <item><description><b>Initial render</b>: build the full layout, render it to a
///     <see cref="ScrollViewer"/> + <see cref="StackPanel"/>, and tag each top-level
///     <c>StackPanel.Children</c> entry with the matching AST node's
///     <see cref="AstNode.StructuralHash"/> via <see cref="Control.Tag"/>.</description></item>
///   <item><description><b>Subsequent renders</b>: run
///     <see cref="AstDiffer.DiffSections"/> to identify Modified sections;
///     for each one rebuild only that section's layout + control and
///     replace <c>panel.Children[i]</c>. Added / Removed / metadata changes
///     fall back to a full re-render.</description></item>
/// </list>
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
    /// Full render. The returned control's top-level StackPanel children
    /// are tagged so a subsequent <see cref="RenderIncremental"/> call can
    /// diff against this document.
    /// </summary>
    public Control Render(DocumentNode document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var layout = _layoutBuilder.Build(document);
        var control = _renderer.Render(layout);
        TagPanelChildren(control, document);
        return control;
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

        // Structural changes (insert / delete) shift section indices, which
        // would invalidate the tagged panel positions. Cheaper and safer to
        // start over.
        if (HasStructuralChange(diff))
            return Render(newDoc);

        // No changes at all → nothing to do.
        if (AllUnchanged(diff))
            return previousControl;

        var panel = ExtractTopPanel(previousControl);
        if (panel is null)
            return Render(newDoc);

        // For every Modified section, rebuild the layout for that single
        // AST child and replace the corresponding panel child. Tag the
        // fresh control with the new hash so the next diff can identify
        // it.
        foreach (var entry in diff)
        {
            if (entry.ChangeType != AstDiffChangeType.Modified) continue;
            if (entry.Index < 0 || entry.Index >= panel.Children.Count) continue;
            if (entry.NewNode is null) continue;

            var newControl = BuildSectionControl(entry.NewNode);
            if (newControl is null) continue;

            TagControl(newControl, entry.Index, entry.NewNode.StructuralHash);
            panel.Children[entry.Index] = newControl;
        }
        return previousControl;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Renders a single top-level AST child by building a synthetic
    /// <see cref="DocumentLayout"/> that contains just its layout. Returns
    /// null when the AST kind doesn't map to a layout the renderer knows.
    /// </summary>
    private Control? BuildSectionControl(AstNode node)
    {
        // The LayoutBuilder works on a DocumentNode; build a temporary one
        // that holds only this child so we can call the same public API.
        var temp = new DocumentNode();
        temp.AddChild(node);

        var layout = _layoutBuilder.Build(temp);
        if (layout.Children.Count == 0) return null;
        return _renderer.Render(layout.Children[0]);
    }

    /// <summary>
    /// Tags each top-level <c>StackPanel.Children</c> entry with its
    /// section index + structural hash via <see cref="Control.Tag"/>.
    /// The tag is read back by <see cref="RenderIncremental"/> via
    /// <see cref="AstDiffer.DiffSections"/>'s positional matching.
    /// </summary>
    private static void TagPanelChildren(Control rendered, DocumentNode document)
    {
        var panel = ExtractTopPanel(rendered);
        if (panel is null) return;

        int n = Math.Min(panel.Children.Count, document.Children.Count);
        for (int i = 0; i < n; i++)
        {
            if (panel.Children[i] is Control c)
                TagControl(c, i, document.Children[i].StructuralHash);
        }
    }

    private static void TagControl(Control c, int index, int hash)
    {
        c.Tag = new SectionTag(index, hash);
    }

    /// <summary>
    /// Walks the rendered control returned by <see cref="AvaloniaRenderer.Render(DocumentLayout)"/>
    /// to find the top-level <see cref="StackPanel"/> that holds one
    /// child per AST section. The renderer wraps the panel in a
    /// <see cref="ScrollViewer"/>, so this peels the layer.
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
    /// Tag payload attached to top-level panel children. Carries both the
    /// section index (positional matching) and the structural hash
    /// (equality check for diagnostics / future invalidation work).
    /// </summary>
    public readonly record struct SectionTag(int Index, int StructuralHash);
}
