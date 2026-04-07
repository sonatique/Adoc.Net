using System.Text;
using AdocNet.Ast;
using AdocNet.Extensions;

namespace AdocNet.Tests;

[TestFixture]
public class OutputProcessorTests
{
    private static AdocEngine CreateEngine(IDocumentRenderer? renderer = null)
    {
        var r = renderer ?? new WritingRenderer("hello");
        return new AdocEngine(r, _ => new DocumentNode());
    }

    [Test]
    public void RegisterOutputProcessor_InvokedAfterRendering()
    {
        var processor = new TrackingOutputProcessor();
        var engine = CreateEngine();
        engine.RegisterOutputProcessor(processor);

        using var output = new MemoryStream();
        engine.Convert("input", output);

        Assert.That(processor.InvokedCount, Is.EqualTo(1));
        Assert.That(processor.LastFormat, Is.EqualTo("test"));
    }

    [Test]
    public void OutputProcessor_ModifiesRenderedOutput()
    {
        var engine = CreateEngine();
        engine.RegisterOutputProcessor(new UpperCaseProcessor());

        using var output = new MemoryStream();
        engine.Convert("input", output);

        var result = Encoding.UTF8.GetString(output.ToArray());
        Assert.That(result, Is.EqualTo("HELLO"));
    }

    [Test]
    public void MultipleOutputProcessors_ChainSequentially()
    {
        var engine = CreateEngine();
        engine.RegisterOutputProcessor(new UpperCaseProcessor());
        engine.RegisterOutputProcessor(new AppendProcessor("!"));

        using var output = new MemoryStream();
        engine.Convert("input", output);

        var result = Encoding.UTF8.GetString(output.ToArray());
        Assert.That(result, Is.EqualTo("HELLO!"));
    }

    [Test]
    public void OutputProcessor_RunsOnCachedOutput()
    {
        var processor = new TrackingOutputProcessor();
        var engine = CreateEngine();
        engine.EnableCaching = true;
        engine.RegisterOutputProcessor(processor);

        using var o1 = new MemoryStream();
        engine.Convert("input", o1);
        using var o2 = new MemoryStream();
        engine.Convert("input", o2);

        Assert.That(processor.InvokedCount, Is.EqualTo(2),
            "Output processor should run on every Convert, even cached");
    }

    [Test]
    public void OutputProcessor_ThrowingDoesNotCrash()
    {
        var warnings = new List<string>();
        var engine = CreateEngine();
        engine.OnWarning = msg => warnings.Add(msg);
        engine.RegisterOutputProcessor(new ThrowingProcessor());

        using var output = new MemoryStream();
        Assert.DoesNotThrow(() => engine.Convert("input", output));
        Assert.That(warnings, Has.Count.GreaterThan(0));
    }

    private sealed class WritingRenderer : IDocumentRenderer
    {
        private readonly string _output;
        public string Format => "test";
        public WritingRenderer(string output) => _output = output;
        public void Render(DocumentNode document, Stream output, RenderOptions options)
        {
            var bytes = Encoding.UTF8.GetBytes(_output);
            output.Write(bytes, 0, bytes.Length);
        }
    }

    private sealed class TrackingOutputProcessor : IOutputProcessor
    {
        public int InvokedCount { get; private set; }
        public string? LastFormat { get; private set; }
        public byte[] Process(byte[] renderedOutput, string format)
        {
            InvokedCount++;
            LastFormat = format;
            return renderedOutput;
        }
    }

    private sealed class UpperCaseProcessor : IOutputProcessor
    {
        public byte[] Process(byte[] renderedOutput, string format)
        {
            var text = Encoding.UTF8.GetString(renderedOutput);
            return Encoding.UTF8.GetBytes(text.ToUpperInvariant());
        }
    }

    private sealed class AppendProcessor : IOutputProcessor
    {
        private readonly string _suffix;
        public AppendProcessor(string suffix) => _suffix = suffix;
        public byte[] Process(byte[] renderedOutput, string format)
        {
            var text = Encoding.UTF8.GetString(renderedOutput) + _suffix;
            return Encoding.UTF8.GetBytes(text);
        }
    }

    private sealed class ThrowingProcessor : IOutputProcessor
    {
        public byte[] Process(byte[] renderedOutput, string format)
            => throw new InvalidOperationException("boom");
    }
}
