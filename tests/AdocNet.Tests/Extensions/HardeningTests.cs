using AdocNet.Ast;
using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class HardeningTests
{
    // ── ExtensionState ──────────────────────────────────────────────────

    [Test]
    public void ExtensionState_Has4Values()
    {
        var values = Enum.GetValues(typeof(ExtensionState));
        Assert.That(values.Length, Is.EqualTo(4));
    }

    [Test]
    public void ExtensionState_ContainsExpectedValues()
    {
        Assert.That(Enum.IsDefined(typeof(ExtensionState), ExtensionState.Loaded), Is.True);
        Assert.That(Enum.IsDefined(typeof(ExtensionState), ExtensionState.Failed), Is.True);
        Assert.That(Enum.IsDefined(typeof(ExtensionState), ExtensionState.Disabled), Is.True);
        Assert.That(Enum.IsDefined(typeof(ExtensionState), ExtensionState.Incompatible), Is.True);
    }

    // ── ExtensionLoadResult ─────────────────────────────────────────────

    [Test]
    public void ExtensionLoadResult_Loaded_HasProcessors()
    {
        var processors = new List<object> { new object(), new object() };
        var result = new ExtensionLoadResult("test-ext", ExtensionState.Loaded, null, processors);

        Assert.That(result.Name, Is.EqualTo("test-ext"));
        Assert.That(result.State, Is.EqualTo(ExtensionState.Loaded));
        Assert.That(result.FailureReason, Is.Null);
        Assert.That(result.Processors, Has.Count.EqualTo(2));
    }

    [Test]
    public void ExtensionLoadResult_Failed_HasReason()
    {
        var result = new ExtensionLoadResult("bad-ext", ExtensionState.Failed,
            "Not a valid .NET assembly", null);

        Assert.That(result.Name, Is.EqualTo("bad-ext"));
        Assert.That(result.State, Is.EqualTo(ExtensionState.Failed));
        Assert.That(result.FailureReason, Is.EqualTo("Not a valid .NET assembly"));
        Assert.That(result.Processors, Is.Empty);
    }

    [Test]
    public void ExtensionLoadResult_Incompatible_NoProcessors()
    {
        var result = new ExtensionLoadResult("future-ext", ExtensionState.Incompatible,
            "Requires API version 2.0", null);

        Assert.That(result.State, Is.EqualTo(ExtensionState.Incompatible));
        Assert.That(result.Processors, Is.Empty);
    }

    [Test]
    public void ExtensionLoadResult_NullName_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ExtensionLoadResult(null!, ExtensionState.Loaded, null, null));
    }

    // ── API Version Constant ────────────────────────────────────────────

    [Test]
    public void AdocEngine_ExtensionApiVersion_IsDefined()
    {
        Assert.That(AdocEngine.ExtensionApiVersion, Is.Not.Null.And.Not.Empty);
        Assert.That(AdocEngine.ExtensionApiVersion, Is.EqualTo("1.0"));
    }

    // ── Manifest apiVersion field ───────────────────────────────────────

    [Test]
    public void Manifest_WithApiVersion_ParsedCorrectly()
    {
        var json = """
            {
              "name": "my-ext",
              "entry": "My.dll",
              "apiVersion": "1.0"
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/ext/my-ext", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.ApiVersion, Is.EqualTo("1.0"));
    }

    [Test]
    public void Manifest_WithoutApiVersion_ReturnsNull()
    {
        var json = """
            {
              "name": "my-ext",
              "entry": "My.dll"
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/ext/my-ext", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.ApiVersion, Is.Null);
    }

    [Test]
    public void Manifest_EmptyApiVersion_ReturnsNull()
    {
        var json = """
            {
              "name": "my-ext",
              "entry": "My.dll",
              "apiVersion": "  "
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/ext/my-ext", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.ApiVersion, Is.Null);
    }

    // ── API Version Compatibility ───────────────────────────────────────

    [Test]
    public void IsApiVersionCompatible_SameVersion_True()
    {
        Assert.That(ExtensionDirectoryLoader.IsApiVersionCompatible("1.0", "1.0"), Is.True);
    }

    [Test]
    public void IsApiVersionCompatible_HigherHostMinor_True()
    {
        Assert.That(ExtensionDirectoryLoader.IsApiVersionCompatible("1.1", "1.0"), Is.True);
    }

    [Test]
    public void IsApiVersionCompatible_LowerHostMinor_False()
    {
        Assert.That(ExtensionDirectoryLoader.IsApiVersionCompatible("1.0", "1.1"), Is.False);
    }

    [Test]
    public void IsApiVersionCompatible_DifferentMajor_False()
    {
        Assert.That(ExtensionDirectoryLoader.IsApiVersionCompatible("1.0", "2.0"), Is.False);
    }

    [Test]
    public void IsApiVersionCompatible_NullExtVersion_True()
    {
        Assert.That(ExtensionDirectoryLoader.IsApiVersionCompatible("1.0", null), Is.True);
    }

    // ── Failure-Based Disabling ─────────────────────────────────────────

    private sealed class StubRenderer : IDocumentRenderer
    {
        public string Format => "stub";
        public void Render(DocumentNode document, Stream output, RenderOptions options) { }
    }

    private sealed class ThrowingBlockProcessor : IBlockProcessor
    {
        public int InvokeCount { get; private set; }
        public bool CanProcess(BlockNode node) => true;
        public void Process(BlockNode node, RenderContext context)
        {
            InvokeCount++;
            throw new InvalidOperationException("always fails");
        }
    }

    private sealed class CountingBlockProcessor : IBlockProcessor
    {
        public int InvokeCount { get; private set; }
        public bool ShouldThrow { get; set; }
        public bool CanProcess(BlockNode node) => true;
        public void Process(BlockNode node, RenderContext context)
        {
            InvokeCount++;
            if (ShouldThrow)
                throw new InvalidOperationException("conditional failure");
        }
    }

    private sealed class TrackingBlockProcessor : IBlockProcessor
    {
        public int InvokeCount { get; private set; }
        public bool CanProcess(BlockNode node) => true;
        public void Process(BlockNode node, RenderContext context)
        {
            InvokeCount++;
        }
    }

    private static DocumentNode MakeSimpleDoc()
    {
        var doc = new DocumentNode();
        var para = new ParagraphNode
        {
            Text = "test",
            Inlines = new List<InlineNode> { new TextInlineNode { Value = "test" } },
        };
        doc.AddChild(para);
        return doc;
    }

    [Test]
    public void Pipeline_ProcessorThrows3Times_Disabled()
    {
        var thrower = new ThrowingBlockProcessor();
        var renderer = new StubRenderer();
        var warnings = new List<string>();
        var engine = new AdocEngine(renderer, _ => MakeSimpleDoc());
        engine.OnWarning = msg => warnings.Add(msg);
        engine.MaxProcessorFailures = 3;
        engine.RegisterBlockProcessor(thrower);

        // 3 Convert() calls, each with 1 paragraph — processor throws once per call
        for (int i = 0; i < 3; i++)
            engine.Convert("", new MemoryStream());

        // Should have been invoked 3 times, then disabled
        Assert.That(thrower.InvokeCount, Is.EqualTo(3));
        Assert.That(warnings, Has.Some.Contains("disabled after 3 consecutive failure"));

        // 4th call — processor should be skipped
        thrower = new ThrowingBlockProcessor(); // can't re-use — already registered the old one
        // Use the same engine — the OLD thrower is in _disabledProcessors
        engine.Convert("", new MemoryStream());

        // The disabled warning should have been emitted exactly once
        var disableWarnings = warnings.Where(w => w.Contains("disabled after")).ToList();
        Assert.That(disableWarnings, Has.Count.EqualTo(1));
    }

    [Test]
    public void Pipeline_DisabledProcessorSkipped()
    {
        var thrower = new ThrowingBlockProcessor();
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => MakeSimpleDoc());
        engine.OnWarning = _ => { };
        engine.MaxProcessorFailures = 1; // Disable after first failure
        engine.RegisterBlockProcessor(thrower);

        engine.Convert("", new MemoryStream()); // 1st call: throws, gets disabled
        Assert.That(thrower.InvokeCount, Is.EqualTo(1));

        engine.Convert("", new MemoryStream()); // 2nd call: should be skipped
        Assert.That(thrower.InvokeCount, Is.EqualTo(1), "Disabled processor should not be invoked again");
    }

    [Test]
    public void Pipeline_FailThenSucceed_CounterResets()
    {
        var processor = new CountingBlockProcessor();
        var renderer = new StubRenderer();
        var warnings = new List<string>();
        var engine = new AdocEngine(renderer, _ => MakeSimpleDoc());
        engine.OnWarning = msg => warnings.Add(msg);
        engine.MaxProcessorFailures = 3;
        engine.RegisterBlockProcessor(processor);

        // Fail twice
        processor.ShouldThrow = true;
        engine.Convert("", new MemoryStream());
        engine.Convert("", new MemoryStream());

        // Succeed — resets counter
        processor.ShouldThrow = false;
        engine.Convert("", new MemoryStream());

        // Fail twice more — should NOT be disabled (counter was reset)
        processor.ShouldThrow = true;
        engine.Convert("", new MemoryStream());
        engine.Convert("", new MemoryStream());

        // Should have been invoked 5 times total, not disabled
        Assert.That(processor.InvokeCount, Is.EqualTo(5));
        Assert.That(warnings, Has.None.Contains("disabled"));
    }

    [Test]
    public void Pipeline_OtherProcessorsUnaffected()
    {
        var thrower = new ThrowingBlockProcessor();
        var tracker = new TrackingBlockProcessor();
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => MakeSimpleDoc());
        engine.OnWarning = _ => { };
        engine.MaxProcessorFailures = 1;
        engine.RegisterBlockProcessor(thrower);
        engine.RegisterBlockProcessor(tracker);

        engine.Convert("", new MemoryStream()); // thrower fails and is disabled
        engine.Convert("", new MemoryStream()); // thrower skipped, tracker still runs

        Assert.That(thrower.InvokeCount, Is.EqualTo(1));
        Assert.That(tracker.InvokeCount, Is.EqualTo(2), "Other processor should still run");
    }

    [Test]
    public void Pipeline_MaxFailures0_NeverDisables()
    {
        var thrower = new ThrowingBlockProcessor();
        var renderer = new StubRenderer();
        var warnings = new List<string>();
        var engine = new AdocEngine(renderer, _ => MakeSimpleDoc());
        engine.OnWarning = msg => warnings.Add(msg);
        engine.MaxProcessorFailures = 0; // beta.8 behavior
        engine.RegisterBlockProcessor(thrower);

        for (int i = 0; i < 10; i++)
            engine.Convert("", new MemoryStream());

        Assert.That(thrower.InvokeCount, Is.EqualTo(10), "Should never be disabled with MaxProcessorFailures=0");
        Assert.That(warnings, Has.None.Contains("disabled"));
    }

    [Test]
    public void Pipeline_MaxFailures1_DisabledAfterFirstFailure()
    {
        var thrower = new ThrowingBlockProcessor();
        var renderer = new StubRenderer();
        var warnings = new List<string>();
        var engine = new AdocEngine(renderer, _ => MakeSimpleDoc());
        engine.OnWarning = msg => warnings.Add(msg);
        engine.MaxProcessorFailures = 1;
        engine.RegisterBlockProcessor(thrower);

        engine.Convert("", new MemoryStream());
        Assert.That(thrower.InvokeCount, Is.EqualTo(1));
        Assert.That(warnings, Has.Some.Contains("disabled after 1 consecutive failure"));

        engine.Convert("", new MemoryStream());
        Assert.That(thrower.InvokeCount, Is.EqualTo(1), "Should be disabled after first failure");
    }

    // ── Safe Loading Methods ────────────────────────────────────────────

    [Test]
    public void LoadExtensionSafe_MissingDll_ReturnsFailedResult()
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());

        var results = engine.LoadExtensionSafe("/nonexistent/extension.dll");

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].State, Is.EqualTo(ExtensionState.Failed));
        Assert.That(results[0].FailureReason, Does.Contain("Extension not found"));
    }

    [Test]
    public void LoadExtensionSafe_InvalidDll_ReturnsFailedResult()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            File.WriteAllText(tempFile, "not a DLL");
            var renderer = new StubRenderer();
            var engine = new AdocEngine(renderer, _ => new DocumentNode());

            var results = engine.LoadExtensionSafe(tempFile);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].State, Is.EqualTo(ExtensionState.Failed));
            Assert.That(results[0].FailureReason, Does.Contain("Not a valid .NET assembly"));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public void LoadExtensionSafe_ValidDll_ReturnsLoadedResult()
    {
        var dllPath = GetTestExtensionDllPath();
        if (dllPath is null)
        {
            Assert.Ignore("TestExtension DLL not found in build output");
            return;
        }

        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());

        var results = engine.LoadExtensionSafe(dllPath);

        Assert.That(results, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(results[0].State, Is.EqualTo(ExtensionState.Loaded));
        Assert.That(results[0].Processors, Has.Count.GreaterThan(0));
    }

    [Test]
    public void LoadExtensionSafe_StillRegistersProcessors()
    {
        var dllPath = GetTestExtensionDllPath();
        if (dllPath is null)
        {
            Assert.Ignore("TestExtension DLL not found in build output");
            return;
        }

        var doc = MakeSimpleDoc();
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => doc);

        engine.LoadExtensionSafe(dllPath);
        engine.Convert("", new MemoryStream());

        // TestPrefixBlockProcessor sets Id = "test-processed"
        var para = (ParagraphNode)doc.Children[0];
        Assert.That(para.Id, Is.EqualTo("test-processed"));
    }

    [Test]
    public void LoadExtensionSafe_AfterConvert_Throws()
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());
        engine.RegisterBlockProcessor(new TrackingBlockProcessor());
        engine.Convert("", new MemoryStream());

        Assert.Throws<InvalidOperationException>(() =>
            engine.LoadExtensionSafe("/any/path.dll"));
    }

    // ── CLI ext status parsing ──────────────────────────────────────────

    [Test]
    public void ExtStatus_ParsesCorrectly()
    {
        // Test that the CLI arg parsing recognizes "ext status"
        var args = new[] { "ext", "status" };
        var result = AdocNet.Cli.ExtensionCommands.ParseExtArguments(args);
        Assert.That(result, Is.InstanceOf<AdocNet.Cli.CliArgs.Ext.ExtStatus>());
    }

    // ── Combined safety test ────────────────────────────────────────────

    [Test]
    public void Pipeline_MixedWorkingAndFailing_OutputStillProduced()
    {
        var thrower = new ThrowingBlockProcessor();
        var tracker = new TrackingBlockProcessor();
        var renderer = new StubRenderer();
        var warnings = new List<string>();

        var engine = new AdocEngine(renderer, _ => MakeSimpleDoc());
        engine.OnWarning = msg => warnings.Add(msg);
        engine.MaxProcessorFailures = 2;
        engine.RegisterBlockProcessor(thrower);
        engine.RegisterBlockProcessor(tracker);

        // Round 1: thrower fails (count=1), tracker works
        using (var ms = new MemoryStream())
            engine.Convert("", ms);

        Assert.That(thrower.InvokeCount, Is.EqualTo(1));
        Assert.That(tracker.InvokeCount, Is.EqualTo(1));

        // Round 2: thrower fails again (count=2, now disabled), tracker works
        using (var ms = new MemoryStream())
            engine.Convert("", ms);

        Assert.That(thrower.InvokeCount, Is.EqualTo(2));
        Assert.That(tracker.InvokeCount, Is.EqualTo(2));
        Assert.That(warnings, Has.Some.Contains("disabled after 2 consecutive failure"));

        // Round 3: thrower skipped (disabled), tracker still works, output produced
        using (var ms = new MemoryStream())
            engine.Convert("", ms);

        Assert.That(thrower.InvokeCount, Is.EqualTo(2), "Disabled thrower should not run");
        Assert.That(tracker.InvokeCount, Is.EqualTo(3), "Working processor should continue");
    }

    [Test]
    public void Pipeline_DefaultMaxFailures_Is3()
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());
        Assert.That(engine.MaxProcessorFailures, Is.EqualTo(3));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static string? GetTestExtensionDllPath()
    {
        var testDir = Path.GetDirectoryName(typeof(HardeningTests).Assembly.Location)!;
        var configDir = Path.GetDirectoryName(testDir)!;
        var config = Path.GetFileName(configDir);
        var extensionDir = Path.Combine(testDir, "..", "..", "..", "..",
            "AdocNet.TestExtension", "bin", config!, "net10.0");
        var path = Path.GetFullPath(Path.Combine(extensionDir, "AdocNet.TestExtension.dll"));
        return File.Exists(path) ? path : null;
    }
}
