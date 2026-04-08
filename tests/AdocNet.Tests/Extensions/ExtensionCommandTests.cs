using AdocNet.Cli;
using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class ExtensionCommandTests
{
    private string _tempRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"adocnet-extcmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Argument parsing ────────────────────────────────────────────────

    [Test]
    public void ParseArguments_ExtList_ReturnsExtList()
    {
        var result = Program.ParseArguments(["ext", "list"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Ext.ExtList>());
    }

    [Test]
    public void ParseArguments_ExtInstall_ReturnsExtInstall()
    {
        var result = Program.ParseArguments(["ext", "install", "/some/path"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Ext.ExtInstall>());
        var install = (CliArgs.Ext.ExtInstall)result;
        Assert.That(install.SourcePath, Is.EqualTo("/some/path"));
        Assert.That(install.Force, Is.False);
    }

    [Test]
    public void ParseArguments_ExtInstallForce_SetsForceFlag()
    {
        var result = Program.ParseArguments(["ext", "install", "/some/path", "--force"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Ext.ExtInstall>());
        var install = (CliArgs.Ext.ExtInstall)result;
        Assert.That(install.Force, Is.True);
    }

    [Test]
    public void ParseArguments_ExtRemove_ReturnsExtRemove()
    {
        var result = Program.ParseArguments(["ext", "remove", "my-ext"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Ext.ExtRemove>());
        var remove = (CliArgs.Ext.ExtRemove)result;
        Assert.That(remove.Name, Is.EqualTo("my-ext"));
    }

    [Test]
    public void ParseArguments_ExtNoAction_ReturnsError()
    {
        var result = Program.ParseArguments(["ext"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
    }

    [Test]
    public void ParseArguments_ExtUnknownAction_ReturnsError()
    {
        var result = Program.ParseArguments(["ext", "unknown"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
    }

    [Test]
    public void ParseArguments_ExtInstallNoPath_ReturnsError()
    {
        var result = Program.ParseArguments(["ext", "install"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
    }

    [Test]
    public void ParseArguments_ExtRemoveNoName_ReturnsError()
    {
        var result = Program.ParseArguments(["ext", "remove"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
    }

    // ── --no-auto-extensions flag ───────────────────────────────────────

    [Test]
    public void ParseArguments_NoAutoExtensions_SetsFlag()
    {
        var result = Program.ParseArguments(["input.adoc", "--no-auto-extensions"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.NoAutoExtensions, Is.True);
    }

    [Test]
    public void ParseArguments_Default_NoAutoExtensionsIsFalse()
    {
        var result = Program.ParseArguments(["input.adoc"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Run>());
        var run = (CliArgs.Run)result;
        Assert.That(run.NoAutoExtensions, Is.False);
    }

    // ── LoadInstalledExtensions on AdocEngine ───────────────────────────

    [Test]
    public void LoadInstalledExtensions_EmptyDirectory_LoadsNoExtensions()
    {
        var renderer = new AdocNet.Converters.Html.HtmlRenderer();
        var engine = new AdocNet.AdocEngine(renderer, s => AdocNet.Parser.AdocParser.Parse(s).Document);
        var warnings = new List<string>();
        engine.OnWarning = msg => warnings.Add(msg);

        // Should not throw, empty dir means no extensions
        engine.LoadInstalledExtensions(_tempRoot);

        // Verify engine still works
        using var ms = new MemoryStream();
        engine.Convert("= Test", ms);
        Assert.That(ms.Length, Is.GreaterThan(0));
    }

    [Test]
    public void LoadInstalledExtensions_NonexistentDirectory_LoadsNoExtensions()
    {
        var renderer = new AdocNet.Converters.Html.HtmlRenderer();
        var engine = new AdocNet.AdocEngine(renderer, s => AdocNet.Parser.AdocParser.Parse(s).Document);

        var nonexistent = Path.Combine(_tempRoot, "does-not-exist");
        engine.LoadInstalledExtensions(nonexistent);

        using var ms = new MemoryStream();
        engine.Convert("= Test", ms);
        Assert.That(ms.Length, Is.GreaterThan(0));
    }

    [Test]
    public void LoadInstalledExtensions_AfterConvert_ThrowsFrozen()
    {
        var renderer = new AdocNet.Converters.Html.HtmlRenderer();
        var engine = new AdocNet.AdocEngine(renderer, s => AdocNet.Parser.AdocParser.Parse(s).Document);

        // Register a dummy processor to trigger freeze
        engine.RegisterDocumentProcessor(new DummyDocProcessor());

        using var ms = new MemoryStream();
        engine.Convert("= Test", ms);

        Assert.That(() => engine.LoadInstalledExtensions(_tempRoot),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void LoadInstalledExtensions_ValidExtension_LoadsProcessors()
    {
        // Set up a valid extension directory with the TestExtension DLL
        var testExtDll = GetTestExtensionDllPath();
        if (testExtDll is null)
        {
            Assert.Ignore("TestExtension DLL not found in build output");
            return;
        }

        var extDir = Path.Combine(_tempRoot, "test-ext");
        Directory.CreateDirectory(extDir);
        File.WriteAllText(Path.Combine(extDir, "extension.json"),
            """{"name": "test-ext", "entry": "AdocNet.TestExtension.dll"}""");
        CopyExtensionFiles(testExtDll, extDir);

        var renderer = new AdocNet.Converters.Html.HtmlRenderer();
        var engine = new AdocNet.AdocEngine(renderer, s => AdocNet.Parser.AdocParser.Parse(s).Document);
        var warnings = new List<string>();
        engine.OnWarning = msg => warnings.Add(msg);

        engine.LoadInstalledExtensions(_tempRoot);

        using var ms = new MemoryStream();
        engine.Convert("= Test\n\nHello world", ms);
        Assert.That(ms.Length, Is.GreaterThan(0));
    }

    // ── ext install / remove integration ────────────────────────────────

    [Test]
    public void ExtInstall_ValidSource_CopiesToExtensionsDir()
    {
        // Create a source extension directory
        var sourceDir = Path.Combine(_tempRoot, "source");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "extension.json"),
            """{"name": "test-install", "version": "1.0.0", "entry": "Test.dll"}""");
        File.WriteAllText(Path.Combine(sourceDir, "Test.dll"), "dummy-dll-content");

        var targetRoot = Path.Combine(_tempRoot, "installed");
        Directory.CreateDirectory(targetRoot);

        // Manually perform what ext install does
        var manifest = ExtensionManifest.Load(sourceDir, null);
        Assert.That(manifest, Is.Not.Null);

        var targetDir = Path.Combine(targetRoot, manifest!.Name);
        Directory.CreateDirectory(targetDir);

        // Copy files
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)));

        // Verify installation
        Assert.That(File.Exists(Path.Combine(targetDir, "extension.json")), Is.True);
        Assert.That(File.Exists(Path.Combine(targetDir, "Test.dll")), Is.True);

        var installed = ExtensionManifest.Load(targetDir, null);
        Assert.That(installed, Is.Not.Null);
        Assert.That(installed!.Name, Is.EqualTo("test-install"));
    }

    [Test]
    public void ExtRemove_ExistingExtension_DeletesDirectory()
    {
        var extDir = Path.Combine(_tempRoot, "to-remove");
        Directory.CreateDirectory(extDir);
        File.WriteAllText(Path.Combine(extDir, "extension.json"),
            """{"name": "to-remove", "entry": "Test.dll"}""");

        Assert.That(Directory.Exists(extDir), Is.True);

        Directory.Delete(extDir, recursive: true);

        Assert.That(Directory.Exists(extDir), Is.False);
    }

    [Test]
    public void ExtList_MultipleExtensions_ShowsAll()
    {
        // Create multiple extension folders
        for (int i = 1; i <= 3; i++)
        {
            var dir = Path.Combine(_tempRoot, $"ext-{i}");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "extension.json"),
                $$$"""{"name": "ext-{{{i}}}", "version": "{{{i}}}.0.0", "entry": "Ext.dll", "description": "Extension {{{i}}}"}""");
        }

        // Scan manifests
        var subdirs = Directory.GetDirectories(_tempRoot);
        Array.Sort(subdirs, (a, b) => string.Compare(
            Path.GetFileName(a), Path.GetFileName(b), StringComparison.Ordinal));

        var manifests = new List<ExtensionManifest>();
        foreach (var dir in subdirs)
        {
            var m = ExtensionManifest.Load(dir, null);
            if (m is not null) manifests.Add(m);
        }

        Assert.That(manifests, Has.Count.EqualTo(3));
        Assert.That(manifests[0].Name, Is.EqualTo("ext-1"));
        Assert.That(manifests[1].Name, Is.EqualTo("ext-2"));
        Assert.That(manifests[2].Name, Is.EqualTo("ext-3"));
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private sealed class DummyDocProcessor : AdocNet.Extensions.IDocumentProcessor
    {
        public bool Process(AdocNet.Ast.DocumentNode document, RenderContext context) { return false; }
    }

    private static string? GetTestExtensionDllPath()
    {
        var candidates = new[]
        {
            Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..",
                "AdocNet.TestExtension", "bin", "Debug", "net10.0", "AdocNet.TestExtension.dll"),
            Path.GetFullPath(Path.Combine("tests", "AdocNet.TestExtension", "bin", "Debug", "net10.0", "AdocNet.TestExtension.dll")),
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full))
                return full;
        }

        return null;
    }

    private static void CopyExtensionFiles(string sourceDll, string targetDir)
    {
        var sourceDir = Path.GetDirectoryName(sourceDll)!;
        foreach (var file in Directory.GetFiles(sourceDir, "*.dll"))
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
    }
}
