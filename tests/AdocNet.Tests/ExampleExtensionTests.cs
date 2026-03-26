using AdocNet.Ast;
using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests;

[TestFixture]
public class ExampleExtensionTests
{
    // ── AutoIdBlockProcessor ─────────────────────────────────────────────

    [Test]
    public void AutoIdBlockProcessor_AssignsIdToSectionWithoutId()
    {
        var doc = new DocumentNode();
        var section = new SectionNode { Level = 1, Title = "Hello World" };
        doc.AddChild(section);

        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(new AutoIdBlockProcessor());

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(section.Id, Is.EqualTo("_hello-world"));
    }

    [Test]
    public void AutoIdBlockProcessor_SkipsSectionWithExistingId()
    {
        var doc = new DocumentNode();
        var section = new SectionNode { Level = 1, Title = "Hello", Id = "custom-id" };
        doc.AddChild(section);

        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(new AutoIdBlockProcessor());

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(section.Id, Is.EqualTo("custom-id"));
    }

    [Test]
    public void AutoIdBlockProcessor_UsesCustomPrefix()
    {
        var doc = new DocumentNode();
        var section = new SectionNode { Level = 1, Title = "Test" };
        doc.AddChild(section);

        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(new AutoIdBlockProcessor("sec-"));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(section.Id, Is.EqualTo("sec-test"));
    }

    [Test]
    public void AutoIdBlockProcessor_DoesNotAffectParagraphs()
    {
        var doc = new DocumentNode();
        var para = new ParagraphNode { Text = "hello" };
        doc.AddChild(para);

        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(new AutoIdBlockProcessor());

        using var output = new MemoryStream();
        engine.Convert("test", output);

        // Paragraph has no Id set — processor should skip it
        Assert.That(para.Id, Is.Null);
    }

    // ── IconMacroProcessor ───────────────────────────────────────────────

    [Test]
    public void IconMacroProcessor_ReplacesHeartIcon()
    {
        var macro = new InlineMacroNode { Name = "icon", Target = "heart", Content = "" };
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "icon:heart[]",
            Inlines = new List<InlineNode> { macro },
        });

        var engine = CreateEngine(doc);
        engine.RegisterInlineProcessor(new IconMacroProcessor());

        using var output = new MemoryStream();
        engine.Convert("test", output);

        // The macro should have been replaced in the Inlines list
        // We verify by checking the paragraph's Inlines contains a TextInlineNode with heart
        var para = (ParagraphNode)doc.Children[0];
        Assert.That(para.Inlines, Has.Count.EqualTo(1));
        Assert.That(para.Inlines[0], Is.InstanceOf<TextInlineNode>());
        Assert.That(((TextInlineNode)para.Inlines[0]).Value, Is.EqualTo("\u2764"));
    }

    [Test]
    public void IconMacroProcessor_IgnoresNonIconMacros()
    {
        var macro = new InlineMacroNode { Name = "kbd", Target = "Ctrl", Content = "C" };
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "kbd:Ctrl[C]",
            Inlines = new List<InlineNode> { macro },
        });

        var engine = CreateEngine(doc);
        engine.RegisterInlineProcessor(new IconMacroProcessor());

        using var output = new MemoryStream();
        engine.Convert("test", output);

        var para = (ParagraphNode)doc.Children[0];
        Assert.That(para.Inlines[0], Is.SameAs(macro), "Non-icon macros should be untouched");
    }

    [Test]
    public void IconMacroProcessor_UnknownIcon_ProducesBracketedName()
    {
        var macro = new InlineMacroNode { Name = "icon", Target = "unknown", Content = "" };
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "icon:unknown[]",
            Inlines = new List<InlineNode> { macro },
        });

        var engine = CreateEngine(doc);
        engine.RegisterInlineProcessor(new IconMacroProcessor());

        using var output = new MemoryStream();
        engine.Convert("test", output);

        var para = (ParagraphNode)doc.Children[0];
        var text = (TextInlineNode)para.Inlines[0];
        Assert.That(text.Value, Is.EqualTo("[unknown]"));
    }

    // ── DocumentMetadataProcessor ────────────────────────────────────────

    [Test]
    public void DocumentMetadataProcessor_InsertsMetadataAtStart()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "original" });

        var engine = CreateEngine(doc);
        engine.RegisterDocumentProcessor(new DocumentMetadataProcessor("Generated by AdocNet"));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(doc.Children, Has.Count.EqualTo(2));
        var inserted = (ParagraphNode)doc.Children[0];
        Assert.That(inserted.Text, Is.EqualTo("Generated by AdocNet"));
        var original = (ParagraphNode)doc.Children[1];
        Assert.That(original.Text, Is.EqualTo("original"));
    }

    [Test]
    public void DocumentMetadataProcessor_EmptyDocument_InsertsParagraph()
    {
        var doc = new DocumentNode();

        var engine = CreateEngine(doc);
        engine.RegisterDocumentProcessor(new DocumentMetadataProcessor("metadata"));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(doc.Children, Has.Count.EqualTo(1));
        Assert.That(((ParagraphNode)doc.Children[0]).Text, Is.EqualTo("metadata"));
    }

    // ── Backward compatibility ───────────────────────────────────────────

    [Test]
    public void WithoutExtensions_OutputUnchanged()
    {
        var doc = new DocumentNode();
        var section = new SectionNode { Level = 1, Title = "Test" };
        doc.AddChild(section);

        var engine = CreateEngine(doc);

        using var output = new MemoryStream();
        engine.Convert("test", output);

        // Section should have no Id (AutoIdBlockProcessor not registered)
        Assert.That(section.Id, Is.Null);
        // Document should have 1 child (DocumentMetadataProcessor not registered)
        Assert.That(doc.Children, Has.Count.EqualTo(1));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static AdocEngine CreateEngine(DocumentNode doc)
        => new(new StubRenderer(), _ => doc);

    private sealed class StubRenderer : IDocumentRenderer
    {
        public string Format => "stub";
        public void Render(DocumentNode document, Stream output, RenderOptions options) { }
    }
}
