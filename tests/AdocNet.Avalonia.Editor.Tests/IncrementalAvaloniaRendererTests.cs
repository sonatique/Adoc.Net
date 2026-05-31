using System.Text;
using global::Avalonia.Controls;
using global::Avalonia.Controls.Documents;
using global::Avalonia.Headless.NUnit;
using AdocNet.Ast;
using AdocNet.Avalonia;
using AdocNet.Parser;

namespace AdocNet.Avalonia.Editor.Tests;

/// <summary>
/// Behavioural tests for the section-level incremental Avalonia renderer.
/// Verify that:
/// <list type="bullet">
///   <item><description>Initial render tags each top-level child with a
///     <c>SectionTag</c> carrying the matching AST hash.</description></item>
///   <item><description>Editing one section produces an updated control
///     for that section only — other children retain their references.</description></item>
///   <item><description>Adding / removing sections falls back to a full
///     re-render so section indices stay coherent.</description></item>
///   <item><description>Changing document metadata (title, attributes)
///     falls back to a full re-render.</description></item>
///   <item><description>Two identical parses produce a no-op
///     (incremental returns the same control reference).</description></item>
/// </list>
/// </summary>
[TestFixture]
public class IncrementalAvaloniaRendererTests
{
    private static StackPanel TopPanel(Control rendered)
    {
        return rendered switch
        {
            ScrollViewer sv when sv.Content is StackPanel sp => sp,
            StackPanel sp => sp,
            _ => throw new InvalidOperationException("Expected ScrollViewer or StackPanel at top of render."),
        };
    }

    private static DocumentNode Parse(string text) => AdocParser.Parse(text).Document;

    [AvaloniaTest]
    public void Initial_render_tags_each_top_level_child_with_section_tag()
    {
        var doc = Parse("para one\n\npara two\n\npara three");
        var r = new IncrementalAvaloniaRenderer();
        var control = r.Render(doc);

        var panel = TopPanel(control);
        Assert.That(panel.Children.Count, Is.EqualTo(doc.Children.Count));

        for (int i = 0; i < panel.Children.Count; i++)
        {
            var child = panel.Children[i] as Control;
            Assert.That(child, Is.Not.Null);
            Assert.That(child!.Tag, Is.InstanceOf<IncrementalAvaloniaRenderer.SectionTag>());
            var tag = (IncrementalAvaloniaRenderer.SectionTag)child.Tag!;
            Assert.That(tag.Index, Is.EqualTo(i));
            Assert.That(tag.StructuralHash, Is.EqualTo(doc.Children[i].StructuralHash));
        }
    }

    [AvaloniaTest]
    public void Editing_one_paragraph_only_replaces_that_section_control()
    {
        var oldDoc = Parse("first para\n\nsecond para\n\nthird para");
        var r = new IncrementalAvaloniaRenderer();
        var control = r.Render(oldDoc);
        var panel = TopPanel(control);

        // Capture references before the update.
        var beforeChild0 = panel.Children[0];
        var beforeChild1 = panel.Children[1];
        var beforeChild2 = panel.Children[2];

        // Change just the middle paragraph.
        var newDoc = Parse("first para\n\nsecond para EDITED\n\nthird para");
        var updated = r.RenderIncremental(oldDoc, newDoc, control);

        // Incremental update reuses the existing control instance.
        Assert.That(updated, Is.SameAs(control));

        // Unchanged sections retained their controls byte-for-byte.
        Assert.That(panel.Children[0], Is.SameAs(beforeChild0));
        Assert.That(panel.Children[2], Is.SameAs(beforeChild2));

        // The edited section got a freshly built control.
        Assert.That(panel.Children[1], Is.Not.SameAs(beforeChild1));

        // …and the freshly built control carries the new hash.
        var newTag = (IncrementalAvaloniaRenderer.SectionTag)((Control)panel.Children[1]).Tag!;
        Assert.That(newTag.Index, Is.EqualTo(1));
        Assert.That(newTag.StructuralHash, Is.EqualTo(newDoc.Children[1].StructuralHash));
    }

    [AvaloniaTest]
    public void Adding_a_section_falls_back_to_full_render()
    {
        var oldDoc = Parse("one\n\ntwo");
        var r = new IncrementalAvaloniaRenderer();
        var control = r.Render(oldDoc);
        var panelBefore = TopPanel(control);
        int beforeCount = panelBefore.Children.Count;

        var newDoc = Parse("one\n\ntwo\n\nthree");
        var updated = r.RenderIncremental(oldDoc, newDoc, control);

        // Structural change produces a brand-new control tree.
        Assert.That(updated, Is.Not.SameAs(control));
        Assert.That(TopPanel(updated).Children.Count, Is.EqualTo(newDoc.Children.Count));
        Assert.That(TopPanel(updated).Children.Count, Is.GreaterThan(beforeCount));
    }

    [AvaloniaTest]
    public void Removing_a_section_falls_back_to_full_render()
    {
        var oldDoc = Parse("one\n\ntwo\n\nthree");
        var r = new IncrementalAvaloniaRenderer();
        var control = r.Render(oldDoc);

        var newDoc = Parse("one\n\nthree");
        var updated = r.RenderIncremental(oldDoc, newDoc, control);

        Assert.That(updated, Is.Not.SameAs(control));
        Assert.That(TopPanel(updated).Children.Count, Is.EqualTo(newDoc.Children.Count));
    }

    [AvaloniaTest]
    public void Changing_document_title_falls_back_to_full_render()
    {
        var oldDoc = Parse("= Original Title\n\nbody");
        var r = new IncrementalAvaloniaRenderer();
        var control = r.Render(oldDoc);

        var newDoc = Parse("= Different Title\n\nbody");
        var updated = r.RenderIncremental(oldDoc, newDoc, control);

        Assert.That(updated, Is.Not.SameAs(control),
            "Title change must trigger a full re-render — section numbering, " +
            "auto-injected attributes, and other doc-spanning state depend on it.");
    }

    [AvaloniaTest]
    public void All_unchanged_returns_existing_control_without_mutating()
    {
        var doc = Parse("alpha\n\nbeta");
        var r = new IncrementalAvaloniaRenderer();
        var control = r.Render(doc);
        var panel = TopPanel(control);
        var beforeChild0 = panel.Children[0];
        var beforeChild1 = panel.Children[1];

        // Parsing the same text twice gives equivalent documents — no diff.
        var sameDoc = Parse("alpha\n\nbeta");
        var updated = r.RenderIncremental(doc, sameDoc, control);

        Assert.That(updated, Is.SameAs(control));
        Assert.That(panel.Children[0], Is.SameAs(beforeChild0));
        Assert.That(panel.Children[1], Is.SameAs(beforeChild1));
    }

    // ── Sectioned documents ───────────────────────────────────────────────
    // Regression coverage for the section-flattening bug: LayoutBuilder
    // expands one SectionNode into a heading + N body blocks, so the number
    // of visual blocks does not match the number of AST children. The
    // renderer must keep a 1:1 mapping between AST children and tagged
    // containers, and must not drop the section body.

    [AvaloniaTest]
    public void Sectioned_document_maps_each_ast_child_to_one_container()
    {
        var doc = Parse("== Section A\n\npara one\n\npara two\n");

        // Precondition for the test to be meaningful: the section is a single
        // top-level AST child that expands to multiple layout blocks.
        Assert.That(doc.Children.Count, Is.EqualTo(1));
        Assert.That(doc.Children[0], Is.InstanceOf<SectionNode>());

        var r = new IncrementalAvaloniaRenderer();
        var panel = TopPanel(r.Render(doc));

        // One container per AST child — NOT one per flattened block (old bug
        // produced 3 panel children here: heading + two paragraphs).
        Assert.That(panel.Children.Count, Is.EqualTo(doc.Children.Count));

        var container = (StackPanel)panel.Children[0];
        var tag = (IncrementalAvaloniaRenderer.SectionTag)container.Tag!;
        Assert.That(tag.Index, Is.EqualTo(0));
        Assert.That(tag.StructuralHash, Is.EqualTo(doc.Children[0].StructuralHash));

        // The whole section lives inside the one container: heading + body.
        Assert.That(container.Children.Count, Is.GreaterThanOrEqualTo(3),
            "section heading and both body paragraphs must all be inside the container");
        var text = AllText(container);
        Assert.That(text, Does.Contain("Section A"));
        Assert.That(text, Does.Contain("para one"));
        Assert.That(text, Does.Contain("para two"));
    }

    [AvaloniaTest]
    public void Editing_paragraph_inside_section_updates_container_and_keeps_body()
    {
        var oldDoc = Parse("== Section A\n\npara one\n\npara two\n");
        var r = new IncrementalAvaloniaRenderer();
        var control = r.Render(oldDoc);
        var panel = TopPanel(control);
        var before = panel.Children[0];

        // Edit a paragraph *inside* the section.
        var newDoc = Parse("== Section A\n\npara one EDITED\n\npara two\n");
        var updated = r.RenderIncremental(oldDoc, newDoc, control);

        // Incremental (not a full re-render) and the section container rebuilt.
        Assert.That(updated, Is.SameAs(control));
        Assert.That(panel.Children.Count, Is.EqualTo(1));
        Assert.That(panel.Children[0], Is.Not.SameAs(before));

        var container = (StackPanel)panel.Children[0];
        var text = AllText(container);
        Assert.That(text, Does.Contain("para one EDITED"));
        // The old bug rebuilt only the heading and dropped the section body,
        // so "para two" would vanish from the preview after the edit.
        Assert.That(text, Does.Contain("para two"));
        Assert.That(text, Does.Contain("Section A"));

        var tag = (IncrementalAvaloniaRenderer.SectionTag)container.Tag!;
        Assert.That(tag.StructuralHash, Is.EqualTo(newDoc.Children[0].StructuralHash));
    }

    [AvaloniaTest]
    public void Multi_section_edit_only_rebuilds_the_changed_section()
    {
        var oldDoc = Parse("== A\n\naaa\n\n== B\n\nbbb\n");
        Assert.That(oldDoc.Children.Count, Is.EqualTo(2));

        var r = new IncrementalAvaloniaRenderer();
        var control = r.Render(oldDoc);
        var panel = TopPanel(control);
        Assert.That(panel.Children.Count, Is.EqualTo(2));
        var beforeA = panel.Children[0];
        var beforeB = panel.Children[1];

        var newDoc = Parse("== A\n\naaa\n\n== B\n\nbbb EDITED\n");
        var updated = r.RenderIncremental(oldDoc, newDoc, control);

        Assert.That(updated, Is.SameAs(control));
        Assert.That(panel.Children[0], Is.SameAs(beforeA), "unchanged section is reused as-is");
        Assert.That(panel.Children[1], Is.Not.SameAs(beforeB), "edited section is rebuilt");
        Assert.That(AllText((Control)panel.Children[1]), Does.Contain("bbb EDITED"));
        Assert.That(AllText((Control)panel.Children[1]), Does.Contain("B"));
    }

    [AvaloniaTest]
    public void Document_title_offset_does_not_break_section_container_mapping()
    {
        var oldDoc = Parse("= Doc Title\n\n== Section A\n\npara one\n\npara two\n");
        Assert.That(oldDoc.Title, Is.EqualTo("Doc Title"));
        Assert.That(oldDoc.Children.Count, Is.EqualTo(1));

        var r = new IncrementalAvaloniaRenderer();
        var control = r.Render(oldDoc);
        var panel = TopPanel(control);

        // Leading (untagged) title block + one tagged section container.
        Assert.That(panel.Children.Count, Is.EqualTo(oldDoc.Children.Count + 1));

        // Editing the section body must update the right container even
        // though the title block shifts its absolute position to index 1.
        var newDoc = Parse("= Doc Title\n\n== Section A\n\npara one EDITED\n\npara two\n");
        var updated = r.RenderIncremental(oldDoc, newDoc, control);

        Assert.That(updated, Is.SameAs(control), "title unchanged → incremental update, not a full re-render");
        var container = FindTaggedContainer(panel, 0);
        Assert.That(container, Is.Not.Null);
        var text = AllText(container!);
        Assert.That(text, Does.Contain("para one EDITED"));
        Assert.That(text, Does.Contain("para two"));
    }

    // ── Inline source-range mapping (E3) ──────────────────────────────────

    [AvaloniaTest]
    public void Rendered_inlines_carry_source_ranges_for_hit_testing()
    {
        var doc = Parse("Hello *bold* world");
        var layout = new AdocNet.Layout.Builders.LayoutBuilder().Build(doc);
        var control = new AvaloniaRenderer { WrapInScrollViewer = false }.Render(layout);

        var panel = (StackPanel)control;
        var paragraph = (TextBlock)panel.Children[0];

        // Every rendered inline should expose a non-None source range so an
        // editor can map it (and a click into it) back to a source offset.
        Assert.That(paragraph.Inlines, Is.Not.Null.And.Not.Empty);
        foreach (var inline in paragraph.Inlines!)
            Assert.That(AvaloniaRenderer.GetSourceRange(inline).IsNone, Is.False,
                $"rendered inline {inline.GetType().Name} should carry a source range");
    }

    // ── Text-extraction helpers for assertions ────────────────────────────

    private static StackPanel? FindTaggedContainer(StackPanel panel, int astIndex)
    {
        foreach (var child in panel.Children)
        {
            if (child is Control c && c.Tag is IncrementalAvaloniaRenderer.SectionTag tag && tag.Index == astIndex)
                return c as StackPanel;
        }
        return null;
    }

    private static string AllText(Control control)
    {
        var sb = new StringBuilder();
        Collect(control, sb);
        return sb.ToString();
    }

    private static void Collect(object? node, StringBuilder sb)
    {
        switch (node)
        {
            case TextBlock tb:
                if (tb.Inlines != null)
                    foreach (var inline in tb.Inlines)
                        CollectInline(inline, sb);
                if (!string.IsNullOrEmpty(tb.Text))
                    sb.Append(tb.Text);
                break;
            case Panel p:
                foreach (var child in p.Children)
                    Collect(child, sb);
                break;
            case ContentControl cc:
                Collect(cc.Content, sb);
                break;
            case Decorator d:
                Collect(d.Child, sb);
                break;
        }
    }

    private static void CollectInline(Inline inline, StringBuilder sb)
    {
        switch (inline)
        {
            case Run run:
                sb.Append(run.Text);
                break;
            case Span span:
                foreach (var child in span.Inlines)
                    CollectInline(child, sb);
                break;
            case InlineUIContainer uic:
                Collect(uic.Child, sb);
                break;
        }
    }
}
