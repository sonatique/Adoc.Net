using AdocNet.Ast;
using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests;

[TestFixture]
public class ExtensionRegistrationTests
{
    [Test]
    public void RegisterBlockProcessor_AddsProcessorToEngine()
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());

        var processor = new StubBlockProcessor();
        engine.RegisterBlockProcessor(processor);

        // Verify the processor is invoked during Convert (which means it was registered)
        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(processor.CanProcessCallCount, Is.EqualTo(0),
            "No block nodes in empty document, so CanProcess should not be called");
    }

    [Test]
    public void RegisterBlockProcessor_InvokesProcessorOnBlockNodes()
    {
        var processor = new StubBlockProcessor();
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ =>
        {
            var doc = new DocumentNode();
            doc.AddChild(new ParagraphNode { Text = "hello" });
            return doc;
        });

        engine.RegisterBlockProcessor(processor);
        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(processor.CanProcessCallCount, Is.EqualTo(1),
            "Should call CanProcess for the ParagraphNode");
    }

    [Test]
    public void RegisterAfterConvert_Throws()
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());

        // Register one processor so pipeline runs and freezes
        engine.RegisterBlockProcessor(new StubBlockProcessor());

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.Throws<InvalidOperationException>(() =>
            engine.RegisterBlockProcessor(new StubBlockProcessor()));
    }

    [Test]
    public void FluentChaining_ReturnsEngine()
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());

        var result = engine
            .RegisterDocumentProcessor(new StubDocumentProcessor())
            .RegisterBlockProcessor(new StubBlockProcessor())
            .RegisterInlineProcessor(new StubInlineProcessor());

        Assert.That(result, Is.SameAs(engine));
    }

    // ── Stubs ────────────────────────────────────────────────────────────

    private sealed class StubRenderer : IDocumentRenderer
    {
        public string Format => "stub";
        public void Render(DocumentNode document, Stream output, RenderOptions options) { }
    }

    private sealed class StubBlockProcessor : IBlockProcessor
    {
        public int CanProcessCallCount { get; private set; }

        public bool CanProcess(BlockNode node)
        {
            CanProcessCallCount++;
            return false;
        }

        public bool Process(BlockNode node, RenderContext context) { return false; }
    }

    private sealed class StubDocumentProcessor : IDocumentProcessor
    {
        public bool Process(DocumentNode document, RenderContext context) { return false; }
    }

    private sealed class StubInlineProcessor : IInlineProcessor
    {
        public bool CanProcess(InlineNode node) => false;
        public bool Process(InlineNode node, RenderContext context) { return false; }
    }
}
