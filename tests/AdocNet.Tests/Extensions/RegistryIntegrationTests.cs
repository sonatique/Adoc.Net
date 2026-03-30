using AdocNet.Cli;
using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class RegistryIntegrationTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"adocnet-regint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── AdocEngine.GetInstalledExtensions ──────────────────────────────

    [Test]
    public void GetInstalledExtensions_WithExtensions_ReturnsInfo()
    {
        CreateExtensionOnDisk("my-ext", "1.0.0", "My extension");

        var extensions = AdocNet.AdocEngine.GetInstalledExtensions(_tempDir);

        Assert.That(extensions, Has.Count.EqualTo(1));
        Assert.That(extensions[0].Name, Is.EqualTo("my-ext"));
        Assert.That(extensions[0].Version, Is.EqualTo("1.0.0"));
    }

    [Test]
    public void GetInstalledExtensions_Empty_ReturnsEmpty()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "extensions"));

        var extensions = AdocNet.AdocEngine.GetInstalledExtensions(_tempDir);

        Assert.That(extensions, Has.Count.EqualTo(0));
    }

    // ── AdocEngine.FindExtension ───────────────────────────────────────

    [Test]
    public void FindExtension_Existing_ReturnsInfo()
    {
        CreateExtensionOnDisk("diagram", "2.0.0", "Diagram support");

        var info = AdocNet.AdocEngine.FindExtension("diagram", _tempDir);

        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Name, Is.EqualTo("diagram"));
        Assert.That(info.Version, Is.EqualTo("2.0.0"));
    }

    [Test]
    public void FindExtension_Nonexistent_ReturnsNull()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "extensions"));

        var info = AdocNet.AdocEngine.FindExtension("nonexistent", _tempDir);

        Assert.That(info, Is.Null);
    }

    // ── ext info parsing ───────────────────────────────────────────────

    [Test]
    public void ParseArguments_ExtInfo_ReturnsExtInfo()
    {
        var result = Program.ParseArguments(["ext", "info", "my-ext"]);

        Assert.That(result, Is.InstanceOf<CliArgs.Ext.ExtInfo>());
        var info = (CliArgs.Ext.ExtInfo)result;
        Assert.That(info.Name, Is.EqualTo("my-ext"));
    }

    [Test]
    public void ParseArguments_ExtInfoNoName_ReturnsError()
    {
        var result = Program.ParseArguments(["ext", "info"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
    }

    // ── ext search parsing ─────────────────────────────────────────────

    [Test]
    public void ParseArguments_ExtSearch_ReturnsExtSearch()
    {
        var result = Program.ParseArguments(["ext", "search", "diagram"]);

        Assert.That(result, Is.InstanceOf<CliArgs.Ext.ExtSearch>());
        var search = (CliArgs.Ext.ExtSearch)result;
        Assert.That(search.Keyword, Is.EqualTo("diagram"));
    }

    [Test]
    public void ParseArguments_ExtSearchNoKeyword_ReturnsError()
    {
        var result = Program.ParseArguments(["ext", "search"]);
        Assert.That(result, Is.InstanceOf<CliArgs.Error>());
    }

    // ── ext install updates registry ───────────────────────────────────

    [Test]
    public void ExtInstall_UpdatesRegistry()
    {
        // Create source extension
        var sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "extension.json"),
            """{"name": "installed-ext", "version": "1.0.0", "entry": "Ext.dll", "description": "Installed"}""");
        File.WriteAllText(Path.Combine(sourceDir, "Ext.dll"), "dummy");

        // Simulate install: copy to extensions dir
        var extDir = Path.Combine(_tempDir, "extensions", "installed-ext");
        Directory.CreateDirectory(extDir);
        foreach (var file in Directory.GetFiles(sourceDir))
            File.Copy(file, Path.Combine(extDir, Path.GetFileName(file)));

        // Update registry
        var manifest = ExtensionManifest.Load(extDir, null)!;
        var registry = ExtensionRegistry.Load(_tempDir, null);
        registry.Add(ExtensionInfo.FromManifest(manifest));
        registry.Save();

        // Reload and verify
        var loaded = ExtensionRegistry.Load(_tempDir, null);
        var info = loaded.Find("installed-ext");
        Assert.That(info, Is.Not.Null);
        Assert.That(info!.Version, Is.EqualTo("1.0.0"));
    }

    // ── ext remove updates registry ────────────────────────────────────

    [Test]
    public void ExtRemove_UpdatesRegistry()
    {
        CreateExtensionOnDisk("to-remove", "1.0.0", "Will be removed");

        // Build registry
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        Assert.That(registry.Find("to-remove"), Is.Not.Null);

        // Simulate remove: delete directory, update registry
        Directory.Delete(Path.Combine(_tempDir, "extensions", "to-remove"), recursive: true);
        registry.Remove("to-remove");
        registry.Save();

        // Reload and verify
        var loaded = ExtensionRegistry.Load(_tempDir, null);
        Assert.That(loaded.Find("to-remove"), Is.Null);
    }

    // ── ext list reads from registry ───────────────────────────────────

    [Test]
    public void ExtList_ReadsFromRegistry()
    {
        CreateExtensionOnDisk("alpha", "1.0.0", "Alpha");
        CreateExtensionOnDisk("beta", "2.0.0", "Beta");

        var registry = ExtensionRegistry.Load(_tempDir, null);
        var all = registry.GetAll();

        Assert.That(all, Has.Count.EqualTo(2));
        Assert.That(all[0].Name, Is.EqualTo("alpha"));
        Assert.That(all[1].Name, Is.EqualTo("beta"));
    }

    // ── stale registry triggers rebuild ────────────────────────────────

    [Test]
    public void StaleRegistry_ExtensionRemoved_TriggersRebuild()
    {
        CreateExtensionOnDisk("ext-a", "1.0.0", "Extension A");
        CreateExtensionOnDisk("ext-b", "1.0.0", "Extension B");

        // Build initial registry
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        Assert.That(registry.GetAll(), Has.Count.EqualTo(2));

        // Manually remove ext-b from filesystem (simulating manual deletion)
        Directory.Delete(Path.Combine(_tempDir, "extensions", "ext-b"), recursive: true);

        // Load should detect stale entry and rebuild
        var reloaded = ExtensionRegistry.Load(_tempDir, null);
        Assert.That(reloaded.GetAll(), Has.Count.EqualTo(1));
        Assert.That(reloaded.Find("ext-a"), Is.Not.Null);
        Assert.That(reloaded.Find("ext-b"), Is.Null);
    }

    private void CreateExtensionOnDisk(string name, string version, string description)
    {
        var extDir = Path.Combine(_tempDir, "extensions", name);
        Directory.CreateDirectory(extDir);
        File.WriteAllText(Path.Combine(extDir, "extension.json"),
            $"{{\"name\": \"{name}\", \"version\": \"{version}\", \"description\": \"{description}\", \"entry\": \"{name}.dll\"}}");
    }
}
