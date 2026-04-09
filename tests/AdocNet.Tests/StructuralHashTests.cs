using AdocNet.Ast;

namespace AdocNet.Tests;

[TestFixture]
public class StructuralHashTests
{
    // ── Determinism ──────────────────────────────────────────────────────────

    [Test]
    public void Same_structure_produces_same_hash()
    {
        var a = MakeParagraph("Hello world");
        var b = MakeParagraph("Hello world");
        Assert.That(a.StructuralHash, Is.EqualTo(b.StructuralHash));
    }

    [Test]
    public void Hash_is_stable_across_multiple_accesses()
    {
        var node = MakeParagraph("Test");
        var first = node.StructuralHash;
        var second = node.StructuralHash;
        var third = node.StructuralHash;
        Assert.That(first, Is.EqualTo(second));
        Assert.That(second, Is.EqualTo(third));
    }

    // ── Sensitivity ──────────────────────────────────────────────────────────

    [Test]
    public void Different_text_produces_different_hash()
    {
        var a = MakeParagraph("Hello");
        var b = MakeParagraph("World");
        Assert.That(a.StructuralHash, Is.Not.EqualTo(b.StructuralHash));
    }

    [Test]
    public void Different_node_kind_produces_different_hash()
    {
        // A paragraph and a section with same text should have different hashes
        var para = MakeParagraph("Title");
        var section = new SectionNode { Level = 1, Title = "Title" };
        Assert.That(para.StructuralHash, Is.Not.EqualTo(section.StructuralHash));
    }

    [Test]
    public void Different_children_produce_different_hash()
    {
        var doc1 = new DocumentNode();
        doc1.AddChild(MakeParagraph("First"));

        var doc2 = new DocumentNode();
        doc2.AddChild(MakeParagraph("Second"));

        Assert.That(doc1.StructuralHash, Is.Not.EqualTo(doc2.StructuralHash));
    }

    [Test]
    public void Adding_child_changes_hash()
    {
        var doc1 = new DocumentNode();
        doc1.AddChild(MakeParagraph("Only"));

        var doc2 = new DocumentNode();
        doc2.AddChild(MakeParagraph("Only"));
        doc2.AddChild(MakeParagraph("Extra"));

        Assert.That(doc1.StructuralHash, Is.Not.EqualTo(doc2.StructuralHash));
    }

    [Test]
    public void Child_order_matters()
    {
        var doc1 = new DocumentNode();
        doc1.AddChild(MakeParagraph("A"));
        doc1.AddChild(MakeParagraph("B"));

        var doc2 = new DocumentNode();
        doc2.AddChild(MakeParagraph("B"));
        doc2.AddChild(MakeParagraph("A"));

        Assert.That(doc1.StructuralHash, Is.Not.EqualTo(doc2.StructuralHash));
    }

    // ── Identical subtrees ───────────────────────────────────────────────────

    [Test]
    public void Identical_subtrees_in_different_locations_have_same_hash()
    {
        var para1 = MakeParagraph("Content");
        var para2 = MakeParagraph("Content");

        var doc1 = new DocumentNode();
        doc1.AddChild(para1);

        var doc2 = new DocumentNode();
        doc2.AddChild(MakeParagraph("Other"));
        doc2.AddChild(para2);

        // The paragraph nodes themselves should have identical hashes
        Assert.That(para1.StructuralHash, Is.EqualTo(para2.StructuralHash));
    }

    // ── BlockNode properties ─────────────────────────────────────────────────

    [Test]
    public void Block_id_affects_hash()
    {
        var a = MakeParagraph("Same");
        var b = MakeParagraph("Same");
        b.Id = "my-id";

        Assert.That(a.StructuralHash, Is.Not.EqualTo(b.StructuralHash));
    }

    [Test]
    public void Block_roles_affect_hash()
    {
        var a = MakeParagraph("Same");
        var b = MakeParagraph("Same");
        b.Roles = ["highlight"];

        Assert.That(a.StructuralHash, Is.Not.EqualTo(b.StructuralHash));
    }

    // ── Inline content ───────────────────────────────────────────────────────

    [Test]
    public void Paragraph_inlines_affect_hash()
    {
        var a = new ParagraphNode
        {
            Text = "raw",
            Inlines = [new TextInlineNode { Value = "Hello" }]
        };
        var b = new ParagraphNode
        {
            Text = "raw",
            Inlines = [new TextInlineNode { Value = "World" }]
        };

        Assert.That(a.StructuralHash, Is.Not.EqualTo(b.StructuralHash));
    }

    [Test]
    public void Section_title_inlines_affect_hash()
    {
        var a = new SectionNode
        {
            Level = 1, Title = "Title",
            TitleInlines = [new TextInlineNode { Value = "Title" }]
        };
        var b = new SectionNode
        {
            Level = 1, Title = "Title",
            TitleInlines = [new TextInlineNode { Value = "Different" }]
        };

        Assert.That(a.StructuralHash, Is.Not.EqualTo(b.StructuralHash));
    }

    [Test]
    public void Strong_inline_children_affect_hash()
    {
        var a = new StrongInlineNode
        {
            Children = [new TextInlineNode { Value = "bold" }]
        };
        var b = new StrongInlineNode
        {
            Children = [new TextInlineNode { Value = "other" }]
        };

        Assert.That(a.StructuralHash, Is.Not.EqualTo(b.StructuralHash));
    }

    [Test]
    public void Strong_inline_roles_affect_hash()
    {
        var a = new StrongInlineNode
        {
            Children = [new TextInlineNode { Value = "text" }]
        };
        var b = new StrongInlineNode
        {
            Children = [new TextInlineNode { Value = "text" }],
            Roles = ["custom"]
        };

        Assert.That(a.StructuralHash, Is.Not.EqualTo(b.StructuralHash));
    }

    // ── Invalidation ─────────────────────────────────────────────────────────

    [Test]
    public void InvalidateHash_clears_cached_hash()
    {
        var node = MakeParagraph("Test");
        var original = node.StructuralHash;

        node.InvalidateStructuralHash();

        // After invalidation, the hash is recomputed — for the same node it should
        // be the same value (deterministic), but the _structuralHashComputed flag was reset
        var recomputed = node.StructuralHash;
        Assert.That(recomputed, Is.EqualTo(original));
    }

    [Test]
    public void Hash_reflects_document_structure()
    {
        // Full document with sections and nested content
        var doc1 = new DocumentNode { Title = "Doc" };
        var sec1 = new SectionNode { Level = 1, Title = "Section 1" };
        sec1.AddChild(MakeParagraph("Content A"));
        doc1.AddChild(sec1);

        var doc2 = new DocumentNode { Title = "Doc" };
        var sec2 = new SectionNode { Level = 1, Title = "Section 1" };
        sec2.AddChild(MakeParagraph("Content A"));
        doc2.AddChild(sec2);

        Assert.That(doc1.StructuralHash, Is.EqualTo(doc2.StructuralHash));
    }

    [Test]
    public void Nested_change_propagates_through_hash()
    {
        var doc1 = new DocumentNode { Title = "Doc" };
        var sec1 = new SectionNode { Level = 1, Title = "Section" };
        sec1.AddChild(MakeParagraph("Same"));
        doc1.AddChild(sec1);

        var doc2 = new DocumentNode { Title = "Doc" };
        var sec2 = new SectionNode { Level = 1, Title = "Section" };
        sec2.AddChild(MakeParagraph("Different"));
        doc2.AddChild(sec2);

        // Documents should differ because nested paragraph content differs
        Assert.That(doc1.StructuralHash, Is.Not.EqualTo(doc2.StructuralHash));
        // Sections should differ too
        Assert.That(sec1.StructuralHash, Is.Not.EqualTo(sec2.StructuralHash));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ParagraphNode MakeParagraph(string text)
        => new() { Text = text };
}
