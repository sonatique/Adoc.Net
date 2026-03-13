using AdocNet;
using AdocNet.Ast;

namespace AdocNet.Tests;

[TestFixture]
public class AstNodeTests
{
    // ── DocumentNode ──────────────────────────────────────────────────────────

    [Test]
    public void Document_kind_is_Document()
    {
        Assert.That(new DocumentNode().Kind, Is.EqualTo(AstNodeKind.Document));
    }

    [Test]
    public void Document_starts_with_no_children()
    {
        Assert.That(new DocumentNode().Children, Is.Empty);
    }

    [Test]
    public void Document_source_defaults_to_none()
    {
        Assert.That(new DocumentNode().Source.IsNone, Is.True);
    }

    [Test]
    public void Document_source_can_be_set()
    {
        var range = new SourceRange(new(1, 1), new(10, 1));
        var doc = new DocumentNode { Source = range };
        Assert.That(doc.Source, Is.EqualTo(range));
    }

    // ── SectionNode ───────────────────────────────────────────────────────────

    [Test]
    public void Section_kind_is_Section()
    {
        var section = new SectionNode { Level = 1, Title = "Intro" };
        Assert.That(section.Kind, Is.EqualTo(AstNodeKind.Section));
    }

    [Test]
    public void Section_exposes_level_and_title()
    {
        var section = new SectionNode { Level = 2, Title = "Getting Started" };
        Assert.Multiple(() =>
        {
            Assert.That(section.Level, Is.EqualTo(2));
            Assert.That(section.Title, Is.EqualTo("Getting Started"));
        });
    }

    [Test]
    public void Section_is_a_BlockNode()
    {
        Assert.That(new SectionNode { Level = 1, Title = "X" }, Is.InstanceOf<BlockNode>());
    }

    // ── ParagraphNode ─────────────────────────────────────────────────────────

    [Test]
    public void Paragraph_kind_is_Paragraph()
    {
        var para = new ParagraphNode { Text = "Hello" };
        Assert.That(para.Kind, Is.EqualTo(AstNodeKind.Paragraph));
    }

    [Test]
    public void Paragraph_exposes_text()
    {
        var para = new ParagraphNode { Text = "Some content." };
        Assert.That(para.Text, Is.EqualTo("Some content."));
    }

    [Test]
    public void Paragraph_is_a_BlockNode()
    {
        Assert.That(new ParagraphNode { Text = "x" }, Is.InstanceOf<BlockNode>());
    }

    // ── Children ──────────────────────────────────────────────────────────────

    [Test]
    public void AddChild_adds_to_children_list()
    {
        var doc = new DocumentNode();
        var para = new ParagraphNode { Text = "Hello" };
        doc.AddChild(para);

        Assert.Multiple(() =>
        {
            Assert.That(doc.Children, Has.Count.EqualTo(1));
            Assert.That(doc.Children[0], Is.SameAs(para));
        });
    }

    [Test]
    public void AddChild_throws_on_null()
    {
        var doc = new DocumentNode();
        Assert.Throws<ArgumentNullException>(() => doc.AddChild(null!));
    }

    [Test]
    public void Children_list_is_readonly()
    {
        Assert.That(new DocumentNode().Children, Is.InstanceOf<IReadOnlyList<AstNode>>());
    }
}
