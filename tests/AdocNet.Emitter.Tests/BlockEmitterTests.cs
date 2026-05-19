using AdocNet.Ast;
using AdocNet.Emitter;
using AdocNet.Parser;

namespace AdocNet.Emitter.Tests;

[TestFixture]
public class BlockEmitterTests
{
    private static readonly AsciidocEmitter Emitter = new();

    private static void AssertRoundTrip(string source)
    {
        var emitted = Emitter.Emit(AdocParser.Parse(source).Document);
        var original = AdocParser.Parse(source).Document;
        var reparsed = AdocParser.Parse(emitted).Document;
        Assert.That(reparsed.StructuralHash, Is.EqualTo(original.StructuralHash),
            $"Round-trip mismatch.\nOriginal:\n{source}\n\nEmitted:\n{emitted}");
    }

    // ── Lists ─────────────────────────────────────────────────────────────

    [Test]
    public void Unordered_list_round_trips()
    {
        AssertRoundTrip("* one\n* two\n* three\n");
    }

    [Test]
    public void Ordered_list_round_trips()
    {
        AssertRoundTrip(". first\n. second\n. third\n");
    }

    [Test]
    public void Nested_unordered_list_round_trips()
    {
        AssertRoundTrip("* outer\n** inner one\n** inner two\n* outer two\n");
    }

    [Test]
    public void Description_list_round_trips()
    {
        AssertRoundTrip("Apple::\nA red fruit.\n\nBanana::\nA yellow fruit.\n");
    }

    [Test]
    public void Horizontal_dlist_round_trips()
    {
        AssertRoundTrip("[horizontal]\nApple::\nA red fruit.\n\nBanana::\nA yellow fruit.\n");
    }

    // ── Delimited blocks ──────────────────────────────────────────────────

    [Test]
    public void Listing_block_round_trips()
    {
        AssertRoundTrip("----\nint x = 0;\nint y = 1;\n----\n");
    }

    [Test]
    public void Source_block_round_trips()
    {
        AssertRoundTrip("[source,csharp]\n----\nvar x = 1;\n----\n");
    }

    [Test]
    public void Example_block_round_trips()
    {
        AssertRoundTrip("====\nThis is an example.\n====\n");
    }

    [Test]
    public void Quote_block_round_trips()
    {
        AssertRoundTrip("[quote,Author,Source]\n____\nA wise saying.\n____\n");
    }

    [Test]
    public void Sidebar_block_round_trips()
    {
        AssertRoundTrip("****\nA sidebar note.\n****\n");
    }

    [Test]
    public void Literal_block_round_trips()
    {
        AssertRoundTrip("....\nLiteral content\n....\n");
    }

    // ── Admonitions ───────────────────────────────────────────────────────

    [Test]
    public void Inline_admonition_round_trips()
    {
        AssertRoundTrip("NOTE: This is a note.");
    }

    [Test]
    public void Block_admonition_round_trips()
    {
        AssertRoundTrip("[WARNING]\n====\nBlock-style warning.\n====\n");
    }

    // ── Tables ────────────────────────────────────────────────────────────

    [Test]
    public void Simple_table_round_trips()
    {
        AssertRoundTrip("|===\n|A |B\n|C |D\n|===\n");
    }

    [Test]
    public void Header_table_round_trips()
    {
        AssertRoundTrip("[options=\"header\"]\n|===\n|Name |Age\n|Alice |30\n|===\n");
    }

    // ── Block image ───────────────────────────────────────────────────────

    [Test]
    public void Block_image_round_trips()
    {
        AssertRoundTrip("image::cat.png[A cat]\n");
    }

    [Test]
    public void Block_image_with_dimensions_round_trips()
    {
        AssertRoundTrip("image::cat.png[A cat,200,150]\n");
    }
}
