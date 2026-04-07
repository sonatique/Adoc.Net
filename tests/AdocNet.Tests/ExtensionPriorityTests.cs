using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Extensions;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class ExtensionPriorityTests
{
    private const string SimpleDoc = "= Title\n\nHello world.\n";

    // ── Test processors that record execution order ─────────────────────

    private sealed class OrderTracker
    {
        public List<string> ExecutionOrder { get; } = new();
    }

    private sealed class TaggingProcessor : IDocumentProcessor, IExtensionPriority, IExtensionCapabilities
    {
        private readonly string _tag;
        public int Priority { get; }
        public bool IsDeterministic => true;

        public TaggingProcessor(string tag, int priority)
        {
            _tag = tag;
            Priority = priority;
        }

        public void Process(DocumentNode document)
        {
            // No AST mutation — just track order via a side channel
        }
    }

    private sealed class TrackingDocProcessor : IDocumentProcessor, IExtensionPriority
    {
        private readonly string _name;
        private readonly OrderTracker _tracker;
        public int Priority { get; }

        public TrackingDocProcessor(string name, int priority, OrderTracker tracker)
        {
            _name = name;
            Priority = priority;
            _tracker = tracker;
        }

        public void Process(DocumentNode document)
        {
            _tracker.ExecutionOrder.Add(_name);
        }
    }

    private sealed class DefaultPriorityProcessor : IDocumentProcessor
    {
        private readonly string _name;
        private readonly OrderTracker _tracker;

        public DefaultPriorityProcessor(string name, OrderTracker tracker)
        {
            _name = name;
            _tracker = tracker;
        }

        public void Process(DocumentNode document)
        {
            _tracker.ExecutionOrder.Add(_name);
        }
    }

    // ── Priority ordering ──────────────────────────────────────────────

    [Test]
    public void Priority_LowerValueExecutesFirst()
    {
        var tracker = new OrderTracker();
        var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document);

        // Register in reverse priority order
        engine.RegisterDocumentProcessor(new TrackingDocProcessor("late", 900, tracker));
        engine.RegisterDocumentProcessor(new TrackingDocProcessor("early", 100, tracker));
        engine.RegisterDocumentProcessor(new TrackingDocProcessor("middle", 500, tracker));

        using var ms = new MemoryStream();
        engine.Convert(SimpleDoc, ms);

        Assert.That(tracker.ExecutionOrder, Is.EqualTo(new[] { "early", "middle", "late" }));
    }

    [Test]
    public void Priority_SamePriority_FifoPreserved()
    {
        var tracker = new OrderTracker();
        var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document);

        // All same priority — should preserve registration order
        engine.RegisterDocumentProcessor(new TrackingDocProcessor("first", 500, tracker));
        engine.RegisterDocumentProcessor(new TrackingDocProcessor("second", 500, tracker));
        engine.RegisterDocumentProcessor(new TrackingDocProcessor("third", 500, tracker));

        using var ms = new MemoryStream();
        engine.Convert(SimpleDoc, ms);

        Assert.That(tracker.ExecutionOrder, Is.EqualTo(new[] { "first", "second", "third" }));
    }

    [Test]
    public void Priority_NoPriorityInterface_DefaultsTo1000_FifoPreserved()
    {
        var tracker = new OrderTracker();
        var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document);

        // No IExtensionPriority — all default to 1000, FIFO preserved
        engine.RegisterDocumentProcessor(new DefaultPriorityProcessor("a", tracker));
        engine.RegisterDocumentProcessor(new DefaultPriorityProcessor("b", tracker));
        engine.RegisterDocumentProcessor(new DefaultPriorityProcessor("c", tracker));

        using var ms = new MemoryStream();
        engine.Convert(SimpleDoc, ms);

        Assert.That(tracker.ExecutionOrder, Is.EqualTo(new[] { "a", "b", "c" }));
    }

    [Test]
    public void Priority_MixedWithDefault_PriorityProcessorsFirst()
    {
        var tracker = new OrderTracker();
        var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document);

        // Default (1000) registered first, priority-100 registered second
        engine.RegisterDocumentProcessor(new DefaultPriorityProcessor("default", tracker));
        engine.RegisterDocumentProcessor(new TrackingDocProcessor("early", 100, tracker));

        using var ms = new MemoryStream();
        engine.Convert(SimpleDoc, ms);

        Assert.That(tracker.ExecutionOrder, Is.EqualTo(new[] { "early", "default" }),
            "Priority 100 should execute before default 1000 regardless of registration order");
    }
}
