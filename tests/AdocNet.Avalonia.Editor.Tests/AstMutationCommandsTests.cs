using AdocNet.Ast;
using AdocNet.Avalonia.Editor;
using AdocNet.Parser;

namespace AdocNet.Avalonia.Editor.Tests;

/// <summary>
/// End-to-end tests for the Full-WYSIWYG AST-mutation commands: each
/// one parses a source string, mutates a typed AST property, emits the
/// mutated node via <c>AsciidocEmitter</c>, splices the emitted slice
/// back into the source, and asserts that re-parsing the result yields
/// the expected new AST shape — *with the rest of the document
/// byte-identical to the input*.
/// </summary>
[TestFixture]
public class AstMutationCommandsTests
{
    private static (DocumentNode Doc, string Source) Parse(string source)
        => (AdocParser.Parse(source).Document, source);

    // ── ToggleBlockRole ──────────────────────────────────────────────────

    [Test]
    public void ToggleBlockRole_adds_role_to_paragraph_without_role()
    {
        var (doc, src) = Parse("first.\n\nsecond.\n");
        var newSrc = AstMutationCommands.ToggleBlockRole(src, doc, blockIndex: 1, role: "warning");

        // Re-parse the result: the second paragraph should now carry [.warning].
        var newDoc = AdocParser.Parse(newSrc).Document;
        var paragraphs = newDoc.Children.OfType<ParagraphNode>().ToList();
        Assert.That(paragraphs, Has.Count.EqualTo(2));
        Assert.That(paragraphs[0].Roles, Is.Empty, "first paragraph must be untouched");
        Assert.That(paragraphs[1].Roles, Does.Contain("warning"));
    }

    [Test]
    public void ToggleBlockRole_removes_existing_role()
    {
        var (doc, src) = Parse("[.warning]\nimportant note\n");
        var newSrc = AstMutationCommands.ToggleBlockRole(src, doc, blockIndex: 0, role: "warning");

        var newDoc = AdocParser.Parse(newSrc).Document;
        var paragraph = newDoc.Children.OfType<ParagraphNode>().First();
        Assert.That(paragraph.Roles, Does.Not.Contain("warning"));
    }

    [Test]
    public void ToggleBlockRole_other_blocks_remain_byte_identical()
    {
        // Three paragraphs. Mutating block 1 should leave blocks 0 and 2
        // byte-for-byte unchanged in the resulting source.
        var (doc, src) = Parse("alpha.\n\nbeta.\n\ngamma.\n");
        var newSrc = AstMutationCommands.ToggleBlockRole(src, doc, blockIndex: 1, role: "lead");

        Assert.That(newSrc, Does.Contain("alpha."));
        Assert.That(newSrc, Does.Contain("gamma."));
        Assert.That(newSrc, Does.Contain(".lead"));

        // The bytes of "alpha." appear once and at the start; same for "gamma."
        // at the end — confirms the splice didn't disturb them.
        Assert.That(newSrc.IndexOf("alpha.", StringComparison.Ordinal), Is.EqualTo(0));
        Assert.That(newSrc.EndsWith("gamma.\n", StringComparison.Ordinal),
            "trailing paragraph must survive the splice byte-identical");
    }

    [Test]
    public void ToggleBlockRole_returns_input_unchanged_when_index_out_of_range()
    {
        var (doc, src) = Parse("solo.\n");
        var newSrc = AstMutationCommands.ToggleBlockRole(src, doc, blockIndex: 99, role: "warning");
        Assert.That(newSrc, Is.EqualTo(src));
    }

    // ── DuplicateBlock ───────────────────────────────────────────────────

    [Test]
    public void DuplicateBlock_adds_a_second_copy_after_the_original()
    {
        var (doc, src) = Parse("first paragraph.\n");
        var newSrc = AstMutationCommands.DuplicateBlock(src, doc, blockIndex: 0);

        var newDoc = AdocParser.Parse(newSrc).Document;
        var paragraphs = newDoc.Children.OfType<ParagraphNode>().ToList();
        Assert.That(paragraphs, Has.Count.EqualTo(2));
        Assert.That(paragraphs[0].Text, Is.EqualTo(paragraphs[1].Text));
    }

    [Test]
    public void DuplicateBlock_returns_input_unchanged_when_index_out_of_range()
    {
        var (doc, src) = Parse("solo.\n");
        var newSrc = AstMutationCommands.DuplicateBlock(src, doc, blockIndex: 5);
        Assert.That(newSrc, Is.EqualTo(src));
    }

    // ── PromoteToHeading ─────────────────────────────────────────────────

    [Test]
    public void PromoteToHeading_turns_paragraph_into_section_at_requested_level()
    {
        var (doc, src) = Parse("My Section Title\n");
        var newSrc = AstMutationCommands.PromoteToHeading(src, doc, blockIndex: 0, level: 1);

        var newDoc = AdocParser.Parse(newSrc).Document;
        var section = newDoc.Children.OfType<SectionNode>().FirstOrDefault();
        Assert.That(section, Is.Not.Null);
        Assert.That(section!.Level, Is.EqualTo(1));
        Assert.That(section.Title, Is.EqualTo("My Section Title"));
    }

    [Test]
    public void PromoteToHeading_at_level_2_produces_three_equals_marker()
    {
        var (doc, src) = Parse("Subtitle\n");
        var newSrc = AstMutationCommands.PromoteToHeading(src, doc, blockIndex: 0, level: 2);
        Assert.That(newSrc, Does.Contain("=== Subtitle"));
    }

    [Test]
    public void PromoteToHeading_leaves_non_paragraph_blocks_unchanged()
    {
        // A list is not a paragraph — the mutation should be a no-op.
        var (doc, src) = Parse("* one\n* two\n");
        var newSrc = AstMutationCommands.PromoteToHeading(src, doc, blockIndex: 0, level: 1);
        Assert.That(newSrc, Is.EqualTo(src));
    }
}
