using System.Text;
using AdocNet.Ast;
using AdocNet.Extensions;

namespace AdocNet.Tests;

[TestFixture]
public class ExtensionDiagnosticsTests
{
    [Test]
    public void AddDiagnostic_CollectsDiagnostics()
    {
        var doc = new DocumentNode();
        var context = new RenderContext(doc, RenderOptions.Default);

        context.AddDiagnostic(new Diagnostic(DiagnosticSeverity.Warning, "test warning", SourceRange.None));
        context.AddDiagnostic(new Diagnostic(DiagnosticSeverity.Error, "test error", SourceRange.None));

        Assert.That(context.Diagnostics, Has.Count.EqualTo(2));
        Assert.That(context.Diagnostics[0].Message, Is.EqualTo("test warning"));
        Assert.That(context.Diagnostics[1].Severity, Is.EqualTo(DiagnosticSeverity.Error));
    }

    [Test]
    public void LastExtensionDiagnostics_PopulatedAfterConvert()
    {
        var engine = CreateEngine();
        engine.RegisterBlockProcessor(new DiagnosticEmittingProcessor());

        using var output = new MemoryStream();
        engine.Convert("= Title\n\nParagraph", output);

        // The processor emits a diagnostic for every block it processes
        Assert.That(engine.LastExtensionDiagnostics, Has.Count.GreaterThan(0));
        Assert.That(engine.LastExtensionDiagnostics[0].Message, Is.EqualTo("processed block"));
    }

    [Test]
    public void LastExtensionDiagnostics_EmptyWhenNoExtensions()
    {
        var engine = CreateEngine();

        using var output = new MemoryStream();
        engine.Convert("= Title", output);

        Assert.That(engine.LastExtensionDiagnostics, Is.Empty);
    }

    [Test]
    public void LastExtensionDiagnostics_ClearedOnNextConvert()
    {
        var engine = CreateEngine();
        engine.RegisterBlockProcessor(new DiagnosticEmittingProcessor());

        using var o1 = new MemoryStream();
        engine.Convert("= Title\n\nParagraph", o1);
        var firstCount = engine.LastExtensionDiagnostics.Count;

        // Second call with same input should re-populate
        using var o2 = new MemoryStream();
        engine.Convert("= Title\n\nParagraph", o2);

        Assert.That(engine.LastExtensionDiagnostics.Count, Is.EqualTo(firstCount));
    }

    private static AdocEngine CreateEngine()
    {
        return new AdocEngine(
            new StubRenderer(),
            text => AdocNet.Parser.AdocParser.Parse(text).Document);
    }

    private sealed class StubRenderer : IDocumentRenderer
    {
        public string Format => "stub";
        public void Render(DocumentNode document, Stream output, RenderOptions options)
        {
            var bytes = Encoding.UTF8.GetBytes("rendered");
            output.Write(bytes, 0, bytes.Length);
        }
    }

    private sealed class DiagnosticEmittingProcessor : IBlockProcessor
    {
        public bool CanProcess(BlockNode node) => true;

        public void Process(BlockNode node, RenderContext context)
        {
            context.AddDiagnostic(new Diagnostic(
                DiagnosticSeverity.Info, "processed block", node.Source));
        }
    }
}
