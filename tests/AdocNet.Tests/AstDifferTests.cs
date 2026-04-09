using AdocNet.Ast;
using AdocNet.Editor;

namespace AdocNet.Tests;

[TestFixture]
public class AstDifferTests
{
    // ── Identical documents ──────────────────────────────────────────────────

    [Test]
    public void Identical_documents_all_unchanged()
    {
        var old = MakeDoc("Section 1", "Content A", "Section 2", "Content B");
        var @new = MakeDoc("Section 1", "Content A", "Section 2", "Content B");

        var diff = AstDiffer.DiffSections(old, @new);

        Assert.That(diff, Has.Count.EqualTo(2));
        Assert.That(diff[0].ChangeType, Is.EqualTo(AstDiffChangeType.Unchanged));
        Assert.That(diff[1].ChangeType, Is.EqualTo(AstDiffChangeType.Unchanged));
    }

    // ── Modified section ─────────────────────────────────────────────────────

    [Test]
    public void Modified_section_detected()
    {
        var old = MakeDoc("Section 1", "Content A", "Section 2", "Content B");
        var @new = MakeDoc("Section 1", "Content A", "Section 2", "Content CHANGED");

        var diff = AstDiffer.DiffSections(old, @new);

        Assert.That(diff, Has.Count.EqualTo(2));
        Assert.That(diff[0].ChangeType, Is.EqualTo(AstDiffChangeType.Unchanged));
        Assert.That(diff[1].ChangeType, Is.EqualTo(AstDiffChangeType.Modified));
    }

    // ── Added section ────────────────────────────────────────────────────────

    [Test]
    public void Section_added_at_end()
    {
        var old = MakeDoc("Section 1", "Content A");
        var @new = MakeDoc("Section 1", "Content A", "Section 2", "Content B");

        var diff = AstDiffer.DiffSections(old, @new);

        var unchanged = diff.Where(d => d.ChangeType == AstDiffChangeType.Unchanged).ToList();
        var added = diff.Where(d => d.ChangeType == AstDiffChangeType.Added).ToList();

        Assert.That(unchanged, Has.Count.EqualTo(1));
        Assert.That(added, Has.Count.EqualTo(1));
    }

    // ── Removed section ──────────────────────────────────────────────────────

    [Test]
    public void Section_removed_from_middle()
    {
        var old = MakeDoc("S1", "A", "S2", "B", "S3", "C");
        var @new = MakeDoc("S1", "A", "S3", "C");

        var diff = AstDiffer.DiffSections(old, @new);

        var removed = diff.Where(d => d.ChangeType == AstDiffChangeType.Removed).ToList();
        Assert.That(removed, Has.Count.EqualTo(1));
        Assert.That(removed[0].OldNode, Is.Not.Null);
    }

    // ── All changed ──────────────────────────────────────────────────────────

    [Test]
    public void All_sections_modified()
    {
        var old = MakeDoc("S1", "A", "S2", "B");
        var @new = MakeDoc("S1", "X", "S2", "Y");

        var diff = AstDiffer.DiffSections(old, @new);

        var modified = diff.Where(d => d.ChangeType == AstDiffChangeType.Modified).ToList();
        Assert.That(modified, Has.Count.EqualTo(2));
    }

    // ── Empty documents ──────────────────────────────────────────────────────

    [Test]
    public void Empty_old_doc_all_added()
    {
        var old = new DocumentNode();
        var @new = MakeDoc("S1", "A");

        var diff = AstDiffer.DiffSections(old, @new);

        Assert.That(diff, Has.Count.EqualTo(1));
        Assert.That(diff[0].ChangeType, Is.EqualTo(AstDiffChangeType.Added));
    }

    [Test]
    public void Empty_new_doc_all_removed()
    {
        var old = MakeDoc("S1", "A");
        var @new = new DocumentNode();

        var diff = AstDiffer.DiffSections(old, @new);

        Assert.That(diff, Has.Count.EqualTo(1));
        Assert.That(diff[0].ChangeType, Is.EqualTo(AstDiffChangeType.Removed));
    }

    [Test]
    public void Both_empty_no_diff()
    {
        var diff = AstDiffer.DiffSections(new DocumentNode(), new DocumentNode());
        Assert.That(diff, Is.Empty);
    }

    // ── ID-based matching ────────────────────────────────────────────────────

    [Test]
    public void Sections_matched_by_id_across_reorder()
    {
        var old = new DocumentNode();
        var oldS1 = new SectionNode { Level = 1, Title = "First", Id = "s1" };
        oldS1.AddChild(new ParagraphNode { Text = "A" });
        var oldS2 = new SectionNode { Level = 1, Title = "Second", Id = "s2" };
        oldS2.AddChild(new ParagraphNode { Text = "B" });
        old.AddChild(oldS1);
        old.AddChild(oldS2);

        // New doc has sections in reverse order but same content
        var @new = new DocumentNode();
        var newS2 = new SectionNode { Level = 1, Title = "Second", Id = "s2" };
        newS2.AddChild(new ParagraphNode { Text = "B" });
        var newS1 = new SectionNode { Level = 1, Title = "First", Id = "s1" };
        newS1.AddChild(new ParagraphNode { Text = "A" });
        @new.AddChild(newS2);
        @new.AddChild(newS1);

        var diff = AstDiffer.DiffSections(old, @new);

        // Both should be Unchanged because they match by ID and content is identical
        var unchanged = diff.Where(d => d.ChangeType == AstDiffChangeType.Unchanged).ToList();
        Assert.That(unchanged, Has.Count.EqualTo(2));
    }

    [Test]
    public void Section_added_in_middle_with_ids()
    {
        var old = new DocumentNode();
        old.AddChild(MakeNamedSection("s1", "First", "A"));
        old.AddChild(MakeNamedSection("s3", "Third", "C"));

        var @new = new DocumentNode();
        @new.AddChild(MakeNamedSection("s1", "First", "A"));
        @new.AddChild(MakeNamedSection("s2", "Second", "B"));
        @new.AddChild(MakeNamedSection("s3", "Third", "C"));

        var diff = AstDiffer.DiffSections(old, @new);

        var unchanged = diff.Where(d => d.ChangeType == AstDiffChangeType.Unchanged).ToList();
        var added = diff.Where(d => d.ChangeType == AstDiffChangeType.Added).ToList();

        Assert.That(unchanged, Has.Count.EqualTo(2));
        Assert.That(added, Has.Count.EqualTo(1));
    }

    // ── Non-section top-level blocks ─────────────────────────────────────────

    [Test]
    public void Non_section_blocks_matched_positionally()
    {
        var old = new DocumentNode();
        old.AddChild(new ParagraphNode { Text = "Preamble" });
        old.AddChild(MakeSection("S1", "Content"));

        var @new = new DocumentNode();
        @new.AddChild(new ParagraphNode { Text = "Preamble" });
        @new.AddChild(MakeSection("S1", "Content"));

        var diff = AstDiffer.DiffSections(old, @new);

        Assert.That(diff, Has.Count.EqualTo(2));
        Assert.That(diff.All(d => d.ChangeType == AstDiffChangeType.Unchanged), Is.True);
    }

    [Test]
    public void Diff_entries_have_correct_node_references()
    {
        var old = MakeDoc("S1", "A");
        var @new = MakeDoc("S1", "Changed");

        var diff = AstDiffer.DiffSections(old, @new);

        Assert.That(diff[0].OldNode, Is.SameAs(old.Children[0]));
        Assert.That(diff[0].NewNode, Is.SameAs(@new.Children[0]));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DocumentNode MakeDoc(params string[] titleContentPairs)
    {
        var doc = new DocumentNode();
        for (int i = 0; i < titleContentPairs.Length; i += 2)
        {
            var section = MakeSection(titleContentPairs[i], titleContentPairs[i + 1]);
            doc.AddChild(section);
        }
        return doc;
    }

    private static SectionNode MakeSection(string title, string content)
    {
        var section = new SectionNode { Level = 1, Title = title };
        section.AddChild(new ParagraphNode { Text = content });
        return section;
    }

    private static SectionNode MakeNamedSection(string id, string title, string content)
    {
        var section = new SectionNode { Level = 1, Title = title, Id = id };
        section.AddChild(new ParagraphNode { Text = content });
        return section;
    }
}
