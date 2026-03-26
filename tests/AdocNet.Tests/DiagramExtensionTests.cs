using AdocNet.Ast;
using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests;

[TestFixture]
public class DiagramExtensionTests
{
    [Test]
    public void DiagramBlock_WithAvailableRunner_ReplacedWithImage()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "plantuml",
            Content = "@startuml\nA -> B\n@enduml",
            Title = "Sequence Diagram",
        });

        var runner = new FakeToolRunner(available: true, outputPath: "/images/diagram.png");
        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(new DiagramBlockProcessor(runner, "/images"));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(doc.Children, Has.Count.EqualTo(1));
        Assert.That(doc.Children[0], Is.InstanceOf<BlockImageNode>());

        var image = (BlockImageNode)doc.Children[0];
        Assert.That(image.Target, Is.EqualTo("/images/diagram.png"));
        Assert.That(image.Alt, Is.EqualTo("Sequence Diagram"));
        Assert.That(image.Title, Is.EqualTo("Sequence Diagram"));
    }

    [Test]
    public void DiagramBlock_WithUnavailableRunner_LeftAsCodeBlock()
    {
        var block = new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "mermaid",
            Content = "graph TD; A-->B;",
        };
        var doc = new DocumentNode();
        doc.AddChild(block);

        var runner = new FakeToolRunner(available: false, outputPath: null);
        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(new DiagramBlockProcessor(runner, "/images"));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(doc.Children[0], Is.SameAs(block), "Block should be unchanged");
    }

    [Test]
    public void DiagramBlock_RunnerReturnsNull_LeftAsCodeBlock()
    {
        var block = new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "plantuml",
            Content = "invalid",
        };
        var doc = new DocumentNode();
        doc.AddChild(block);

        var runner = new FakeToolRunner(available: true, outputPath: null);
        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(new DiagramBlockProcessor(runner, "/images"));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(doc.Children[0], Is.SameAs(block), "Block should be unchanged when runner returns null");
    }

    [Test]
    public void DiagramBlock_RunnerThrows_LeftAsCodeBlock()
    {
        var block = new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "ditaa",
            Content = "source",
        };
        var doc = new DocumentNode();
        doc.AddChild(block);

        var runner = new ThrowingToolRunner();
        var engine = CreateEngine(doc);
        engine.OnWarning = _ => { };
        engine.RegisterBlockProcessor(new DiagramBlockProcessor(runner, "/images"));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(doc.Children[0], Is.SameAs(block));
    }

    [Test]
    public void NonDiagramBlock_NotAffected()
    {
        var csharpBlock = new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "csharp",
            Content = "Console.WriteLine(\"hello\");",
        };
        var doc = new DocumentNode();
        doc.AddChild(csharpBlock);

        var runner = new FakeToolRunner(available: true, outputPath: "/images/out.png");
        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(new DiagramBlockProcessor(runner, "/images"));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(doc.Children[0], Is.SameAs(csharpBlock), "C# block should not be processed");
        Assert.That(runner.GenerateCallCount, Is.EqualTo(0));
    }

    [Test]
    public void ListingBlock_NotAffected()
    {
        var block = new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Listing,
            Content = "some listing",
        };
        var doc = new DocumentNode();
        doc.AddChild(block);

        var runner = new FakeToolRunner(available: true, outputPath: "/out.png");
        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(new DiagramBlockProcessor(runner, "/images"));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(doc.Children[0], Is.SameAs(block));
    }

    [Test]
    public void DiagramBlock_PreservesIdFromOriginal()
    {
        var doc = new DocumentNode();
        doc.AddChild(new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = "graphviz",
            Content = "digraph { A -> B }",
            Id = "my-diagram",
        });

        var runner = new FakeToolRunner(available: true, outputPath: "/img/graph.png");
        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(new DiagramBlockProcessor(runner, "/img"));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        var image = (BlockImageNode)doc.Children[0];
        Assert.That(image.Id, Is.EqualTo("my-diagram"));
    }

    [TestCase("plantuml")]
    [TestCase("mermaid")]
    [TestCase("ditaa")]
    [TestCase("graphviz")]
    [TestCase("dot")]
    public void AllDiagramLanguages_Recognized(string language)
    {
        var block = new DelimitedBlockNode
        {
            BlockKind = DelimitedBlockKind.Source,
            Language = language,
            Content = "source",
        };
        var doc = new DocumentNode();
        doc.AddChild(block);

        var runner = new FakeToolRunner(available: true, outputPath: "/out.png");
        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(new DiagramBlockProcessor(runner, "/out"));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(doc.Children[0], Is.InstanceOf<BlockImageNode>(),
            $"Language '{language}' should be recognized as a diagram language");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static AdocEngine CreateEngine(DocumentNode doc)
        => new(new StubRenderer(), _ => doc);

    private sealed class StubRenderer : IDocumentRenderer
    {
        public string Format => "stub";
        public void Render(DocumentNode document, Stream output, RenderOptions options) { }
    }

    private sealed class FakeToolRunner(bool available, string? outputPath) : IDiagramToolRunner
    {
        public int GenerateCallCount { get; private set; }
        public bool IsAvailable => available;

        public string? Generate(string language, string source, string outputDirectory)
        {
            GenerateCallCount++;
            return outputPath;
        }
    }

    private sealed class ThrowingToolRunner : IDiagramToolRunner
    {
        public bool IsAvailable => true;

        public string? Generate(string language, string source, string outputDirectory)
            => throw new InvalidOperationException("Tool crashed");
    }
}
