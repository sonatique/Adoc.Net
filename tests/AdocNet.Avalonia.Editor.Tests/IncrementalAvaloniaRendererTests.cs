using global::Avalonia.Controls;
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
}
