using AdocNet.Ast;
using AdocNet.Emitter;
using AdocNet.Parser;

namespace AdocNet.Emitter.Tests;

[TestFixture]
public class BasicEmitterTests
{
    private static readonly AsciidocEmitter Emitter = new();

    private static string ParseAndEmit(string source)
    {
        var doc = AdocParser.Parse(source).Document;
        return Emitter.Emit(doc);
    }

    private static int HashOf(string source)
    {
        return AdocParser.Parse(source).Document.StructuralHash;
    }

    private static void AssertRoundTrip(string source)
    {
        var emitted = ParseAndEmit(source);
        var reparsed = AdocParser.Parse(emitted).Document;
        var original = AdocParser.Parse(source).Document;

        Assert.That(reparsed.StructuralHash, Is.EqualTo(original.StructuralHash),
            $"Round-trip mismatch.\nOriginal:\n{source}\n\nEmitted:\n{emitted}");
    }

    private static void AssertSourceAnchoredByteIdentical(string source)
    {
        var doc = AdocParser.Parse(source).Document;
        var emitted = Emitter.Emit(doc, new EmitOptions
        {
            PreserveOriginalWhenAvailable = true,
            OriginalSource = source,
        });
        Assert.That(emitted, Is.EqualTo(source),
            "Source-anchored emit must reproduce the original source byte-for-byte for any node with a populated SourceRange.");
    }

    // ── From-AST round-trip — foundation nodes ────────────────────────────

    [Test]
    public void Plain_paragraph_round_trips()
    {
        AssertRoundTrip("Hello world.");
    }

    [Test]
    public void Document_title_round_trips()
    {
        AssertRoundTrip("= My Document\n\nA paragraph.");
    }

    [Test]
    public void Section_levels_round_trip()
    {
        AssertRoundTrip("= Doc\n\n== Level 1\n\n=== Level 2\n\n==== Level 3\n");
    }

    [Test]
    public void Thematic_break_round_trips()
    {
        AssertRoundTrip("Before.\n\n'''\n\nAfter.");
    }

    [Test]
    public void Page_break_round_trips()
    {
        AssertRoundTrip("Before.\n\n<<<\n\nAfter.");
    }

    // ── Inline formatting ─────────────────────────────────────────────────

    [Test]
    public void Inline_strong_round_trips()
    {
        AssertRoundTrip("A *bold* word.");
    }

    [Test]
    public void Inline_emphasis_round_trips()
    {
        AssertRoundTrip("An _italic_ word.");
    }

    [Test]
    public void Inline_monospace_round_trips()
    {
        AssertRoundTrip("A `mono` word.");
    }

    [Test]
    public void Inline_passthrough_round_trips()
    {
        AssertRoundTrip("A +++<raw>html</raw>+++ run.");
    }

    [Test]
    public void Multiple_inlines_round_trip()
    {
        AssertRoundTrip("This is *bold*, _italic_, and `mono`.");
    }

    // ── Source-anchored byte-identical ────────────────────────────────────

    [Test]
    public void Source_anchored_paragraph_is_byte_identical()
    {
        AssertSourceAnchoredByteIdentical("Hello world.");
    }

    [Test]
    public void Source_anchored_section_is_byte_identical()
    {
        AssertSourceAnchoredByteIdentical("= Title\n\nFirst paragraph.\n\n== Section\n\nSecond paragraph.\n");
    }

    [Test]
    public void Source_anchored_mixed_inline_is_byte_identical()
    {
        AssertSourceAnchoredByteIdentical("Some *bold* and _italic_ and `mono` text.\n");
    }

    // ── Document attributes ───────────────────────────────────────────────

    [Test]
    public void Document_attribute_round_trips()
    {
        AssertRoundTrip("= Doc\n:author: Alice\n:version: 1.0\n\nBody.");
    }
}
