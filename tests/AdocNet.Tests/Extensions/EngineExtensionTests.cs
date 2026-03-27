using AdocNet.Ast;
using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class EngineExtensionTests
{
    private sealed class StubRenderer : IDocumentRenderer
    {
        public string Format => "stub";
        public void Render(DocumentNode document, Stream output, RenderOptions options) { }
    }

    [Test]
    public void LoadExtension_CoreAssembly_RegistersAndExecutesProcessors()
    {
        // Load the Core assembly which contains IconMacroProcessor (parameterless ctor).
        // Create a document with an icon:star[] macro and verify the processor runs.
        var assemblyPath = typeof(IconMacroProcessor).Assembly.Location;

        var doc = new DocumentNode();
        var para = new ParagraphNode
        {
            Text = "icon:star[]",
            Inlines = new List<InlineNode> { new InlineMacroNode { Name = "icon", Target = "star", Content = "" } },
        };
        doc.AddChild(para);

        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => doc);

        var warnings = new List<string>();
        engine.OnWarning = msg => warnings.Add(msg);
        engine.LoadExtension(assemblyPath);

        using var output = new MemoryStream();
        engine.Convert("", output);

        // The IconMacroProcessor should have replaced the InlineMacroNode with a TextInlineNode
        Assert.That(para.Inlines, Has.Count.EqualTo(1));
        Assert.That(para.Inlines[0], Is.TypeOf<TextInlineNode>());
        var text = (TextInlineNode)para.Inlines[0];
        Assert.That(text.Value, Is.EqualTo("\u2605"), "icon:star[] should become ★");
    }

    [Test]
    public void LoadExtension_AfterConvert_ThrowsInvalidOperation()
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());

        // Register a processor to trigger the _frozen flag
        engine.RegisterInlineProcessor(new IconMacroProcessor());

        using var output = new MemoryStream();
        engine.Convert("", output);

        // Now LoadExtension should throw because the engine is frozen
        Assert.Throws<InvalidOperationException>(() =>
            engine.LoadExtension(typeof(IconMacroProcessor).Assembly.Location));
    }

    [Test]
    public void LoadExtension_MissingDll_WarnsButDoesNotThrow()
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());

        var warnings = new List<string>();
        engine.OnWarning = msg => warnings.Add(msg);

        // Should not throw
        engine.LoadExtension("/nonexistent/extension.dll");

        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("Extension not found"));
    }

    [Test]
    public void LoadExtensions_Directory_WarnsOnMissingDir()
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());

        var warnings = new List<string>();
        engine.OnWarning = msg => warnings.Add(msg);

        engine.LoadExtensions("/nonexistent/dir/");

        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("Extension directory not found"));
    }

    [Test]
    public void LoadExtension_FluentChaining_ReturnsEngine()
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());

        var result = engine.LoadExtension("/nonexistent/test.dll");

        Assert.That(result, Is.SameAs(engine), "LoadExtension should return the same engine for fluent chaining");
    }

    [Test]
    public void LoadExtensions_FluentChaining_ReturnsEngine()
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());

        var result = engine.LoadExtensions("/nonexistent/dir/");

        Assert.That(result, Is.SameAs(engine), "LoadExtensions should return the same engine for fluent chaining");
    }
}
