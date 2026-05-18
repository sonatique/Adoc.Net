using AdocNet.Avalonia.Editor.ViewModels;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.Avalonia.Editor.Tests;

[TestFixture]
public class CaretContextTests
{
    [Test]
    public void Caret_in_paragraph_resolves_to_paragraph()
    {
        var doc = AdocParser.Parse("Hello world.").Document;
        var node = CaretContext.Resolve(doc, line: 1, column: 4);
        Assert.That(node, Is.Not.Null);
        Assert.That(node, Is.InstanceOf<ParagraphNode>().Or
            .InstanceOf<TextInlineNode>());
    }

    [Test]
    public void Caret_in_section_title_resolves_to_section_or_inline()
    {
        var doc = AdocParser.Parse("= Heading\n\nBody text.\n").Document;
        var node = CaretContext.Resolve(doc, line: 1, column: 4);
        // Source ranges for inlines are populated, so caret on "Heading"
        // resolves to one of: the SectionNode itself, or a TextInlineNode
        // inside its TitleInlines (deepest match).
        Assert.That(node, Is.Not.Null);
        Assert.That(
            node is SectionNode or TextInlineNode,
            $"Expected SectionNode or TextInlineNode, got {node!.Kind}");
    }

    [Test]
    public void Caret_inside_strong_inline_resolves_to_strong()
    {
        var doc = AdocParser.Parse("a *bold* word").Document;
        // Position the caret on the 'b' of "bold" — col 4 in the paragraph.
        var node = CaretContext.Resolve(doc, line: 1, column: 4);
        Assert.That(node, Is.Not.Null);
        Assert.That(
            node is StrongInlineNode or TextInlineNode,
            $"Expected StrongInlineNode or TextInlineNode (deepest under Strong), got {node!.Kind}");
    }

    [Test]
    public void Describe_renders_section_with_level_and_truncated_title()
    {
        var section = new SectionNode { Level = 2, Title = "My Section Title" };
        var label = CaretContext.Describe(section);
        Assert.That(label, Does.StartWith("§2"));
        Assert.That(label, Does.Contain("My Section Title"));
    }

    [Test]
    public void Describe_renders_paragraph_simply()
    {
        var p = new ParagraphNode { Text = "x" };
        Assert.That(CaretContext.Describe(p), Is.EqualTo("paragraph"));
    }

    [Test]
    public void Describe_renders_null_as_empty_string()
    {
        Assert.That(CaretContext.Describe(null), Is.EqualTo(string.Empty));
    }

    [Test]
    public void Resolve_returns_null_for_out_of_range_position()
    {
        var doc = AdocParser.Parse("short").Document;
        var node = CaretContext.Resolve(doc, line: 999, column: 1);
        Assert.That(node, Is.Null);
    }
}
