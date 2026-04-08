using AdocNet.Ast;
using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests;

[TestFixture]
public class PipelineExecutionTests
{
    // ── Step 1: Document processor execution ─────────────────────────────

    [Test]
    public void DocumentProcessor_IsInvokedDuringConvert()
    {
        var processor = new FlagDocumentProcessor();
        var engine = CreateEngine(new DocumentNode());
        engine.RegisterDocumentProcessor(processor);

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(processor.WasInvoked, Is.True);
    }

    [Test]
    public void DocumentProcessor_ReceivesCorrectDocument()
    {
        var doc = new DocumentNode { Title = "Test Doc" };
        DocumentNode? received = null;
        var processor = new DelegateDocumentProcessor(d => received = d);
        var engine = CreateEngine(doc);
        engine.RegisterDocumentProcessor(processor);

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(received, Is.SameAs(doc));
    }

    // ── Step 2: Block processor execution ────────────────────────────────

    [Test]
    public void BlockProcessor_TargetsParagraph_SkipsSection()
    {
        var doc = new DocumentNode();
        var section = new SectionNode { Level = 1, Title = "Heading" };
        section.AddChild(new ParagraphNode { Text = "hello" });
        doc.AddChild(section);

        var processor = new TrackingBlockProcessor<ParagraphNode>();
        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(processor);

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(processor.ProcessedNodes, Has.Count.EqualTo(1));
        Assert.That(processor.ProcessedNodes[0], Is.InstanceOf<ParagraphNode>());
        Assert.That(processor.SkippedCount, Is.GreaterThan(0),
            "Should have called CanProcess on SectionNode and returned false");
    }

    [Test]
    public void BlockProcessor_WalksNestedBlocks()
    {
        var doc = new DocumentNode();
        var section = new SectionNode { Level = 1, Title = "S1" };
        section.AddChild(new ParagraphNode { Text = "p1" });
        section.AddChild(new ParagraphNode { Text = "p2" });
        doc.AddChild(section);

        var processor = new TrackingBlockProcessor<ParagraphNode>();
        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(processor);

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(processor.ProcessedNodes, Has.Count.EqualTo(2));
    }

    // ── Step 3: Inline processor execution ───────────────────────────────

    [Test]
    public void InlineProcessor_TargetsInlineMacro()
    {
        var doc = new DocumentNode();
        var macro = new InlineMacroNode { Name = "icon", Target = "heart", Content = "" };
        var para = new ParagraphNode
        {
            Text = "icon:heart[]",
            Inlines = [new TextInlineNode { Value = "before " }, macro],
        };
        doc.AddChild(para);

        var processor = new TrackingInlineProcessor<InlineMacroNode>();
        var engine = CreateEngine(doc);
        engine.RegisterInlineProcessor(processor);

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(processor.ProcessedNodes, Has.Count.EqualTo(1));
        Assert.That(processor.ProcessedNodes[0], Is.SameAs(macro));
    }

    [Test]
    public void InlineProcessor_WalksNestedInlines()
    {
        // Strong containing a TextInlineNode
        var textNode = new TextInlineNode { Value = "bold text" };
        var strong = new StrongInlineNode { Children = [textNode] };
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode
        {
            Text = "*bold text*",
            Inlines = [strong],
        });

        var processor = new TrackingInlineProcessor<TextInlineNode>();
        var engine = CreateEngine(doc);
        engine.RegisterInlineProcessor(processor);

        using var output = new MemoryStream();
        engine.Convert("test", output);

        // Should find TextInlineNode inside StrongInlineNode
        Assert.That(processor.ProcessedNodes, Has.Count.EqualTo(1));
        Assert.That(processor.ProcessedNodes[0], Is.SameAs(textNode));
    }

    [Test]
    public void InlineProcessor_WalksListItemInlines()
    {
        var textNode = new TextInlineNode { Value = "item text" };
        var listItem = new ListItemNode { Text = "item text", Inlines = [textNode] };
        var list = new ListNode { ListKind = ListKind.Unordered };
        list.AddChild(listItem);
        var doc = new DocumentNode();
        doc.AddChild(list);

        var processor = new TrackingInlineProcessor<TextInlineNode>();
        var engine = CreateEngine(doc);
        engine.RegisterInlineProcessor(processor);

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(processor.ProcessedNodes, Has.Count.EqualTo(1));
    }

    // ── Step 4: Ordering and error tests ─────────────────────────────────

    [Test]
    public void DocumentProcessors_ExecuteInFIFOOrder()
    {
        var order = new List<string>();
        var engine = CreateEngine(new DocumentNode());

        engine.RegisterDocumentProcessor(new DelegateDocumentProcessor(_ => order.Add("first")));
        engine.RegisterDocumentProcessor(new DelegateDocumentProcessor(_ => order.Add("second")));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(order, Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void BlockProcessors_ExecuteInFIFOOrder()
    {
        var order = new List<string>();
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "test" });

        var engine = CreateEngine(doc);
        engine.RegisterBlockProcessor(new DelegateBlockProcessor(
            _ => true, (_, _) => order.Add("first")));
        engine.RegisterBlockProcessor(new DelegateBlockProcessor(
            _ => true, (_, _) => order.Add("second")));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(order, Is.EqualTo(new[] { "first", "second" }));
    }

    [Test]
    public void ThrowingProcessor_ConvertsSuccessfully_WarningInvoked()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "hello" });

        string? warningMessage = null;
        var engine = CreateEngine(doc);
        engine.OnWarning = msg => warningMessage = msg;

        engine.RegisterDocumentProcessor(
            new DelegateDocumentProcessor(_ => throw new ArgumentException("test error")));

        using var output = new MemoryStream();
        Assert.DoesNotThrow(() => engine.Convert("test", output));

        Assert.That(warningMessage, Is.Not.Null);
        Assert.That(warningMessage, Does.Contain("ArgumentException"));
        Assert.That(warningMessage, Does.Contain("test error"));
    }

    [Test]
    public void ThrowingBlockProcessor_ContinuesToNextProcessor()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "hello" });

        var secondRan = false;
        var engine = CreateEngine(doc);
        engine.OnWarning = _ => { };

        engine.RegisterBlockProcessor(new DelegateBlockProcessor(
            _ => true, (_, _) => throw new InvalidOperationException("boom")));
        engine.RegisterBlockProcessor(new DelegateBlockProcessor(
            _ => true, (_, _) => secondRan = true));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(secondRan, Is.True,
            "Second processor should run even after first throws");
    }

    [Test]
    public void CanProcessThrows_TreatedAsFalse_WarningEmitted()
    {
        var doc = new DocumentNode();
        doc.AddChild(new ParagraphNode { Text = "hello" });

        string? warning = null;
        var engine = CreateEngine(doc);
        engine.OnWarning = msg => warning = msg;

        engine.RegisterBlockProcessor(new DelegateBlockProcessor(
            _ => throw new Exception("canprocess fail"),
            (_, _) => Assert.Fail("Process should not be called")));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(warning, Does.Contain("canprocess fail"));
    }

    [Test]
    public void ExecutionOrder_Document_Then_Block_Then_Inline()
    {
        var order = new List<string>();
        var doc = new DocumentNode();
        var text = new TextInlineNode { Value = "hello" };
        doc.AddChild(new ParagraphNode { Text = "hello", Inlines = [text] });

        var engine = CreateEngine(doc);
        engine.RegisterInlineProcessor(new DelegateInlineProcessor(
            _ => true, (_, _) => order.Add("inline")));
        engine.RegisterBlockProcessor(new DelegateBlockProcessor(
            _ => true, (_, _) => order.Add("block")));
        engine.RegisterDocumentProcessor(
            new DelegateDocumentProcessor(_ => order.Add("document")));

        using var output = new MemoryStream();
        engine.Convert("test", output);

        Assert.That(order, Is.EqualTo(new[] { "document", "block", "inline" }));
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static AdocEngine CreateEngine(DocumentNode doc)
    {
        return new AdocEngine(new StubRenderer(), _ => doc);
    }

    // ── Short-circuit tests ────────────────────────────────────────────

    [Test]
    public void DocumentProcessor_ReturnsTrue_SkipsRemainingDocProcessors()
    {
        var doc = MakeSimpleDocument();
        var engine = CreateEngine(doc);
        var first = new ShortCircuitDocProcessor(handled: true);
        var second = new ShortCircuitDocProcessor(handled: false);
        engine.RegisterDocumentProcessor(first);
        engine.RegisterDocumentProcessor(second);
        engine.Convert("ignored", new MemoryStream());
        Assert.That(first.WasInvoked, Is.True);
        Assert.That(second.WasInvoked, Is.False);
    }

    [Test]
    public void DocumentProcessor_ReturnsFalse_ContinuesToNext()
    {
        var doc = MakeSimpleDocument();
        var engine = CreateEngine(doc);
        var first = new ShortCircuitDocProcessor(handled: false);
        var second = new ShortCircuitDocProcessor(handled: false);
        engine.RegisterDocumentProcessor(first);
        engine.RegisterDocumentProcessor(second);
        engine.Convert("ignored", new MemoryStream());
        Assert.That(first.WasInvoked, Is.True);
        Assert.That(second.WasInvoked, Is.True);
    }

    [Test]
    public void BlockProcessor_ReturnsTrue_SkipsRemainingForThatNode()
    {
        var doc = MakeSimpleDocument(); // has a ParagraphNode
        var engine = CreateEngine(doc);
        var first = new ShortCircuitBlockProcessor(handled: true);
        var second = new ShortCircuitBlockProcessor(handled: false);
        engine.RegisterBlockProcessor(first);
        engine.RegisterBlockProcessor(second);
        engine.Convert("ignored", new MemoryStream());
        Assert.That(first.InvokeCount, Is.GreaterThan(0));
        Assert.That(second.InvokeCount, Is.EqualTo(0));
    }

    [Test]
    public void InlineProcessor_ReturnsTrue_SkipsRemainingForThatNode()
    {
        var doc = MakeSimpleDocument(); // has a TextInlineNode
        var engine = CreateEngine(doc);
        var first = new ShortCircuitInlineProcessor(handled: true);
        var second = new ShortCircuitInlineProcessor(handled: false);
        engine.RegisterInlineProcessor(first);
        engine.RegisterInlineProcessor(second);
        engine.Convert("ignored", new MemoryStream());
        Assert.That(first.InvokeCount, Is.GreaterThan(0));
        Assert.That(second.InvokeCount, Is.EqualTo(0));
    }

    [Test]
    public void BlockProcessor_ShortCircuit_IsPerNode()
    {
        // Create doc with two paragraphs
        var doc = new DocumentNode();
        var para1 = new ParagraphNode { Text = "first", Inlines = [new TextInlineNode { Value = "first" }] };
        var para2 = new ParagraphNode { Text = "second", Inlines = [new TextInlineNode { Value = "second" }] };
        doc.AddChild(para1);
        doc.AddChild(para2);

        var engine = CreateEngine(doc);
        // Returns true only for the first paragraph
        var processor = new ConditionalShortCircuitBlockProcessor(para1);
        var tracker = new ShortCircuitBlockProcessor(handled: false);
        engine.RegisterBlockProcessor(processor);
        engine.RegisterBlockProcessor(tracker);
        engine.Convert("ignored", new MemoryStream());

        // tracker should NOT have been called for para1 (short-circuited)
        // but SHOULD have been called for para2
        Assert.That(tracker.InvokeCount, Is.EqualTo(1));
    }

    [Test]
    public void DocumentProcessor_ReceivesRenderContext_CanEmitDiagnostics()
    {
        var doc = MakeSimpleDocument();
        var engine = CreateEngine(doc);
        var processor = new DiagnosticDocProcessor();
        engine.RegisterDocumentProcessor(processor);
        engine.Convert("ignored", new MemoryStream());
        Assert.That(engine.LastExtensionDiagnostics, Has.Count.EqualTo(1));
        Assert.That(engine.LastExtensionDiagnostics[0].Message, Is.EqualTo("doc-processor-diagnostic"));
    }

    private static DocumentNode MakeSimpleDocument()
    {
        var doc = new DocumentNode();
        var para = new ParagraphNode { Text = "Hello", Inlines = [new TextInlineNode { Value = "Hello" }] };
        doc.AddChild(para);
        return doc;
    }

    // ── Stubs ────────────────────────────────────────────────────────────

    private sealed class ShortCircuitDocProcessor(bool handled) : IDocumentProcessor
    {
        public bool WasInvoked { get; private set; }
        public bool Process(DocumentNode document, RenderContext context) { WasInvoked = true; return handled; }
    }

    private sealed class ShortCircuitBlockProcessor(bool handled) : IBlockProcessor
    {
        public int InvokeCount { get; private set; }
        public bool CanProcess(BlockNode node) => true;
        public bool Process(BlockNode node, RenderContext context) { InvokeCount++; return handled; }
    }

    private sealed class ShortCircuitInlineProcessor(bool handled) : IInlineProcessor
    {
        public int InvokeCount { get; private set; }
        public bool CanProcess(InlineNode node) => true;
        public bool Process(InlineNode node, RenderContext context) { InvokeCount++; return handled; }
    }

    private sealed class ConditionalShortCircuitBlockProcessor(BlockNode target) : IBlockProcessor
    {
        public bool CanProcess(BlockNode node) => true;
        public bool Process(BlockNode node, RenderContext context) => ReferenceEquals(node, target);
    }

    private sealed class DiagnosticDocProcessor : IDocumentProcessor
    {
        public bool Process(DocumentNode document, RenderContext context)
        {
            context.AddDiagnostic(new Diagnostic(DiagnosticSeverity.Info, "doc-processor-diagnostic", default));
            return false;
        }
    }

    private sealed class StubRenderer : IDocumentRenderer
    {
        public string Format => "stub";
        public void Render(DocumentNode document, Stream output, RenderOptions options) { }
    }

    private sealed class FlagDocumentProcessor : IDocumentProcessor
    {
        public bool WasInvoked { get; private set; }
        public bool Process(DocumentNode document, RenderContext context) { WasInvoked = true; return false; }
    }

    private sealed class DelegateDocumentProcessor(Action<DocumentNode> action) : IDocumentProcessor
    {
        public bool Process(DocumentNode document, RenderContext context) { action(document); return false; }
    }

    private sealed class DelegateBlockProcessor(
        Func<BlockNode, bool> canProcess,
        Action<BlockNode, RenderContext> process) : IBlockProcessor
    {
        public bool CanProcess(BlockNode node) => canProcess(node);
        public bool Process(BlockNode node, RenderContext context) { process(node, context); return false; }
    }

    private sealed class DelegateInlineProcessor(
        Func<InlineNode, bool> canProcess,
        Action<InlineNode, RenderContext> process) : IInlineProcessor
    {
        public bool CanProcess(InlineNode node) => canProcess(node);
        public bool Process(InlineNode node, RenderContext context) { process(node, context); return false; }
    }

    private sealed class TrackingBlockProcessor<T> : IBlockProcessor where T : BlockNode
    {
        public List<BlockNode> ProcessedNodes { get; } = [];
        public int SkippedCount { get; private set; }

        public bool CanProcess(BlockNode node)
        {
            if (node is T) return true;
            SkippedCount++;
            return false;
        }

        public bool Process(BlockNode node, RenderContext context)
        {
            ProcessedNodes.Add(node);
            return false;
        }
    }

    private sealed class TrackingInlineProcessor<T> : IInlineProcessor where T : InlineNode
    {
        public List<InlineNode> ProcessedNodes { get; } = [];

        public bool CanProcess(InlineNode node) => node is T;

        public bool Process(InlineNode node, RenderContext context)
        {
            ProcessedNodes.Add(node);
            return false;
        }
    }
}
