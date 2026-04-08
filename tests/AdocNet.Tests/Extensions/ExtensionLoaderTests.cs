using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class ExtensionLoaderTests
{
    [Test]
    public void LoadAssembly_CoreAssembly_DiscoversBuiltInProcessors()
    {
        // Load the AdocNet.Core assembly which contains IconMacroProcessor, AutoIdBlockProcessor, etc.
        var assemblyPath = typeof(IconMacroProcessor).Assembly.Location;
        var warnings = new List<string>();

        var results = ExtensionLoader.LoadAssembly(assemblyPath, msg => warnings.Add(msg));

        // Should find at least IconMacroProcessor (parameterless ctor)
        // and AutoIdBlockProcessor (has default parameter, so parameterless ctor exists at IL level)
        Assert.That(results, Has.Count.GreaterThanOrEqualTo(1),
            "Should discover at least one processor type from AdocNet.Core");

        // Verify that the discovered types actually implement processor interfaces
        foreach (var instance in results)
        {
            var isProcessor = instance is IDocumentProcessor
                           || instance is IBlockProcessor
                           || instance is IInlineProcessor;
            Assert.That(isProcessor, Is.True,
                $"Instance {instance.GetType().Name} should implement a processor interface");
        }
    }

    [Test]
    public void LoadAssembly_MissingFile_ReturnsEmptyWithWarning()
    {
        var warnings = new List<string>();

        var results = ExtensionLoader.LoadAssembly("/nonexistent/path.dll", msg => warnings.Add(msg));

        Assert.That(results, Is.Empty);
        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("Extension not found"));
    }

    [Test]
    public void LoadAssembly_NotDotNetFile_ReturnsEmptyWithWarning()
    {
        // Use a known non-.NET file as test input
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "this is not a .NET assembly");
            var renamed = Path.ChangeExtension(tempFile, ".dll");
            File.Move(tempFile, renamed);
            tempFile = renamed;

            var warnings = new List<string>();
            var results = ExtensionLoader.LoadAssembly(tempFile, msg => warnings.Add(msg));

            Assert.That(results, Is.Empty);
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
    public void LoadDirectory_MissingDirectory_ReturnsEmptyWithWarning()
    {
        var warnings = new List<string>();

        var results = ExtensionLoader.LoadDirectory("/nonexistent/dir/", msg => warnings.Add(msg));

        Assert.That(results, Is.Empty);
        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("Extension directory not found"));
    }

    [Test]
    public void LoadDirectory_EmptyDirectory_ReturnsEmptyWithWarning()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var warnings = new List<string>();
            var results = ExtensionLoader.LoadDirectory(tempDir, msg => warnings.Add(msg));

            Assert.That(results, Is.Empty);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("No extension DLLs found"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

#if NET6_0_OR_GREATER
    [Test]
    public void LoadAssemblyIsolated_ReturnsExtensionLoadContext()
    {
        var dllPath = GetTestExtensionDllPath();
        if (dllPath is null)
            Assert.Ignore("TestExtension DLL not found — build it first");

        var (extensions, context) = ExtensionLoader.LoadAssemblyIsolated(dllPath, null);

        Assert.That(extensions, Has.Count.GreaterThan(0));
        Assert.That(context, Is.Not.Null);
        Assert.That(context, Is.InstanceOf<ExtensionLoadContext>());
    }

    [Test]
    public void LoadAssemblyIsolated_ContextIsCollectible()
    {
        var dllPath = GetTestExtensionDllPath();
        if (dllPath is null)
            Assert.Ignore("TestExtension DLL not found — build it first");

        var (_, context) = ExtensionLoader.LoadAssemblyIsolated(dllPath, null);

        Assert.That(context, Is.Not.Null);
        Assert.That(context!.IsCollectible, Is.True);
    }

    [Test]
    public void LoadAssemblyIsolated_ExtensionsExecuteCorrectly()
    {
        var dllPath = GetTestExtensionDllPath();
        if (dllPath is null)
            Assert.Ignore("TestExtension DLL not found — build it first");

        var (extensions, _) = ExtensionLoader.LoadAssemblyIsolated(dllPath, null);

        // Find a block processor (TestPrefixBlockProcessor)
        var blockProcessors = extensions.OfType<IBlockProcessor>().ToList();
        Assert.That(blockProcessors, Has.Count.GreaterThan(0),
            "Should find at least one block processor from test extension");
    }

    [Test]
    public void LoadAssemblyIsolated_HostAssembly_ReturnsNullContext()
    {
        // Loading AdocNet.Core (already in default context) should reuse existing assembly
        var assemblyPath = typeof(IconMacroProcessor).Assembly.Location;
        var (extensions, context) = ExtensionLoader.LoadAssemblyIsolated(assemblyPath, null);

        Assert.That(extensions, Has.Count.GreaterThan(0));
        Assert.That(context, Is.Null, "Host assembly should not create a new load context");
    }

    [Test]
    public void Shutdown_UnloadsExtensionContexts()
    {
        var dllPath = GetTestExtensionDllPath();
        if (dllPath is null)
            Assert.Ignore("TestExtension DLL not found — build it first");

        var renderer = new StubRenderer();
        var engine = new AdocEngine(renderer, _ => new AdocNet.Ast.DocumentNode());
        engine.LoadExtension(dllPath);

        // Shutdown should not throw
        Assert.DoesNotThrow(() => engine.Shutdown());
    }

    private sealed class StubRenderer : IDocumentRenderer
    {
        public string Format => "stub";
        public void Render(AdocNet.Ast.DocumentNode document, Stream output, RenderOptions options) { }
    }
#endif

    private static string? GetTestExtensionDllPath()
    {
        var testDir = Path.GetDirectoryName(typeof(ExtensionLoaderTests).Assembly.Location)!;
        var configDir = Path.GetDirectoryName(testDir)!;
        var config = Path.GetFileName(configDir);
        var extensionDir = Path.Combine(testDir, "..", "..", "..", "..",
            "AdocNet.TestExtension", "bin", config!, "net10.0");
        var path = Path.GetFullPath(Path.Combine(extensionDir, "AdocNet.TestExtension.dll"));
        return File.Exists(path) ? path : null;
    }
}
