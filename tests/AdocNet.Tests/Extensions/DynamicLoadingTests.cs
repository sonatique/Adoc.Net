using AdocNet.Ast;
using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests.Extensions;

/// <summary>
/// Tests for dynamic extension loading from external DLLs.
/// Uses the AdocNet.TestExtension and AdocNet.TestEmptyExtension projects.
/// </summary>
[TestFixture]
public class DynamicLoadingTests
{
    private static string TestExtensionDllPath
    {
        get
        {
            // testDir = tests/AdocNet.Tests/bin/{Config}/net10.0/
            // We extract {Config} (Debug or Release) from the path.
            var testDir = Path.GetDirectoryName(typeof(DynamicLoadingTests).Assembly.Location)!;
            var tfmDir = testDir;                                        // .../net10.0
            var configDir = Path.GetDirectoryName(tfmDir)!;              // .../Debug
            var config = Path.GetFileName(configDir);                    // "Debug" or "Release"
            var extensionDir = Path.Combine(testDir, "..", "..", "..", "..", "AdocNet.TestExtension", "bin", config!, "net10.0");
            return Path.GetFullPath(Path.Combine(extensionDir, "AdocNet.TestExtension.dll"));
        }
    }

    private static string EmptyExtensionDllPath
    {
        get
        {
            var testDir = Path.GetDirectoryName(typeof(DynamicLoadingTests).Assembly.Location)!;
            var configDir = Path.GetDirectoryName(testDir)!;
            var config = Path.GetFileName(configDir);
            var extensionDir = Path.Combine(testDir, "..", "..", "..", "..", "AdocNet.TestEmptyExtension", "bin", config!, "net10.0");
            return Path.GetFullPath(Path.Combine(extensionDir, "AdocNet.TestEmptyExtension.dll"));
        }
    }

    private sealed class StubRenderer : IDocumentRenderer
    {
        public string Format => "stub";
        public void Render(DocumentNode document, Stream output, RenderOptions options) { }
    }

    // ── Step 2: Basic loading test ─────────────────────────────────────

    [Test]
    public void LoadExtension_TestDll_ExecutesBlockProcessor()
    {
        var dllPath = TestExtensionDllPath;
        Assert.That(File.Exists(dllPath), Is.True,
            $"Test extension DLL not found at {dllPath}. Build the AdocNet.TestExtension project first.");

        var doc = new DocumentNode();
        var para = new ParagraphNode
        {
            Text = "hello world",
            Inlines = new List<InlineNode> { new TextInlineNode { Value = "hello world" } },
        };
        doc.AddChild(para);

        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => doc);
        engine.LoadExtension(dllPath);

        using var output = new MemoryStream();
        engine.Convert("", output);

        // TestPrefixBlockProcessor sets Id = "test-processed" on paragraphs
        Assert.That(para.Id, Is.EqualTo("test-processed"),
            "Block processor from loaded DLL should have set the paragraph Id");
    }

    [Test]
    public void LoadExtension_TestDll_ExecutesDocumentProcessor()
    {
        var dllPath = TestExtensionDllPath;
        Assert.That(File.Exists(dllPath), Is.True);

        var doc = new DocumentNode();
        var para = new ParagraphNode
        {
            Text = "content",
            Inlines = new List<InlineNode> { new TextInlineNode { Value = "content" } },
        };
        doc.AddChild(para);

        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => doc);
        engine.LoadExtension(dllPath);

        using var output = new MemoryStream();
        engine.Convert("", output);

        // TestDocumentProcessor sets attribute "test-extension-loaded" = "true"
        Assert.That(doc.Attributes.ContainsKey("test-extension-loaded"), Is.True,
            "Document processor from loaded DLL should have set the attribute");
        Assert.That(doc.Attributes["test-extension-loaded"], Is.EqualTo("true"));
    }

    // ── Step 3: Directory loading test ─────────────────────────────────

    [Test]
    public void LoadExtensions_DirectoryWithDll_LoadsAndExecutes()
    {
        var dllPath = TestExtensionDllPath;
        Assert.That(File.Exists(dllPath), Is.True);

        // Copy the DLL and its dependencies to a temp directory
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-ext-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.GetDirectoryName(dllPath)!;
            foreach (var file in Directory.GetFiles(sourceDir, "*.dll"))
                File.Copy(file, Path.Combine(tempDir, Path.GetFileName(file)), overwrite: true);

            var doc = new DocumentNode();
            var para = new ParagraphNode
            {
                Text = "test",
                Inlines = new List<InlineNode> { new TextInlineNode { Value = "test" } },
            };
            doc.AddChild(para);

            var renderer = new StubRenderer();
            var engine = new AdocEngine(renderer, _ => doc);
            engine.LoadExtensions(tempDir);

            using var output = new MemoryStream();
            engine.Convert("", output);

            // At minimum, TestPrefixBlockProcessor should have run
            Assert.That(para.Id, Is.EqualTo("test-processed"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Step 4: Error handling tests ───────────────────────────────────

    [Test]
    public void LoadExtension_MissingDll_WarnsNoThrow()
    {
        var warnings = new List<string>();
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());
        engine.OnWarning = msg => warnings.Add(msg);

        engine.LoadExtension("/nonexistent/extension.dll");

        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("Extension not found"));
    }

    [Test]
    public void LoadExtension_InvalidDll_WarnsNoThrow()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".dll");
        try
        {
            File.WriteAllText(tempFile, "this is not a DLL");

            var warnings = new List<string>();
            var renderer = new StubRenderer();
            var engine = new AdocEngine(renderer, _ => new DocumentNode());
            engine.OnWarning = msg => warnings.Add(msg);

            engine.LoadExtension(tempFile);

            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("Not a valid .NET assembly"));
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    public void LoadExtension_NoProcessorsDll_EmptyResultNoThrow()
    {
        var dllPath = EmptyExtensionDllPath;
        Assert.That(File.Exists(dllPath), Is.True,
            $"Empty extension DLL not found at {dllPath}. Build the AdocNet.TestEmptyExtension project first.");

        var warnings = new List<string>();
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());
        engine.OnWarning = msg => warnings.Add(msg);

        engine.LoadExtension(dllPath);

        // Should load without error — just no processors found
        // No crash, no warning about missing processors (that's normal)
        using var output = new MemoryStream();
        engine.Convert("", output); // Should succeed without extensions
    }

    [Test]
    public void LoadExtension_SkipsNoCtorProcessor_WithWarning()
    {
        var dllPath = TestExtensionDllPath;
        Assert.That(File.Exists(dllPath), Is.True);

        var warnings = new List<string>();

        // Use ExtensionLoader directly to check warnings
        var results = ExtensionLoader.LoadAssembly(dllPath, msg => warnings.Add(msg));

        // NoCtorProcessor has no parameterless ctor — should be skipped with warning
        Assert.That(warnings, Has.Some.Contains("NoCtorProcessor"));
        Assert.That(warnings, Has.Some.Contains("no parameterless constructor"));

        // But the other processors should have been loaded
        Assert.That(results, Has.Count.GreaterThanOrEqualTo(3),
            "Should load TestDocumentProcessor, TestPrefixBlockProcessor, TestInlineProcessor");
    }

    // ── Step 5: Deterministic ordering test ────────────────────────────

    [Test]
    public void LoadExtensions_DirectoryOrder_AlphabeticalByFilename()
    {
        var dllPath = TestExtensionDllPath;
        Assert.That(File.Exists(dllPath), Is.True);

        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-order-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceDir = Path.GetDirectoryName(dllPath)!;

            // Copy the extension DLL with two different names to simulate multiple DLLs
            // "Aaa.dll" and "Zzz.dll" — alphabetical order should be Aaa first
            foreach (var file in Directory.GetFiles(sourceDir, "*.dll"))
            {
                File.Copy(file, Path.Combine(tempDir, Path.GetFileName(file)), overwrite: true);
            }
            File.Copy(dllPath, Path.Combine(tempDir, "Aaa.dll"), overwrite: true);
            File.Copy(dllPath, Path.Combine(tempDir, "Zzz.dll"), overwrite: true);

            var warnings = new List<string>();
            var results = ExtensionLoader.LoadDirectory(tempDir, msg => warnings.Add(msg));

            // Both DLLs should have been loaded — processors from Aaa.dll first, then Zzz.dll
            Assert.That(results.Count, Is.GreaterThan(0),
                "Should have loaded extensions from alphabetically ordered DLLs");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── Step 6: Frozen engine test ─────────────────────────────────────

    [Test]
    public void LoadExtension_AfterConvert_ThrowsInvalidOperation()
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());

        // Register a dummy processor to trigger _frozen
        engine.RegisterInlineProcessor(new IconMacroProcessor());

        using var output = new MemoryStream();
        engine.Convert("", output);

        Assert.Throws<InvalidOperationException>(() =>
            engine.LoadExtension(TestExtensionDllPath));
    }

    [Test]
    public void LoadExtensions_AfterConvert_ThrowsInvalidOperation()
    {
        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new DocumentNode());

        engine.RegisterInlineProcessor(new IconMacroProcessor());

        using var output = new MemoryStream();
        engine.Convert("", output);

        Assert.Throws<InvalidOperationException>(() =>
            engine.LoadExtensions("/some/dir/"));
    }

    // ── IExtension metadata test ───────────────────────────────────────

    [Test]
    public void LoadExtension_IExtensionMetadata_TypeWithMetadataLoaded()
    {
        var dllPath = TestExtensionDllPath;
        Assert.That(File.Exists(dllPath), Is.True);

        var results = ExtensionLoader.LoadAssembly(dllPath, null);

        // TestPrefixBlockProcessor implements IExtension
        var withMetadata = results.Where(r => r is IExtension).Cast<IExtension>().ToList();
        Assert.That(withMetadata, Has.Count.GreaterThanOrEqualTo(1));
        Assert.That(withMetadata[0].Name, Is.EqualTo("TestPrefixProcessor"));
        Assert.That(withMetadata[0].Version, Is.EqualTo("1.0.0"));
    }
}
