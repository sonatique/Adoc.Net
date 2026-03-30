using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class ExtensionRegistryTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"adocnet-registry-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void SaveAndLoad_RoundTrip_PreservesData()
    {
        CreateExtensionOnDisk("alpha", "1.0.0", "Alpha extension");
        CreateExtensionOnDisk("beta", "2.0.0", "Beta extension");

        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        // Add dependency info (not in manifest, added post-rebuild)
        registry.Remove("beta");
        registry.Add(new ExtensionInfo("beta", "2.0.0", "Beta extension",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "beta")),
            new[] { "alpha >= 1.0.0" }));
        registry.Save();

        var loaded = ExtensionRegistry.Load(_tempDir, null);
        var all = loaded.GetAll();

        Assert.That(all, Has.Count.EqualTo(2));
        Assert.That(all[0].Name, Is.EqualTo("alpha"));
        Assert.That(all[0].Version, Is.EqualTo("1.0.0"));
        Assert.That(all[0].Description, Is.EqualTo("Alpha extension"));
        Assert.That(all[1].Name, Is.EqualTo("beta"));
        Assert.That(all[1].Version, Is.EqualTo("2.0.0"));
        Assert.That(all[1].Dependencies, Has.Count.EqualTo(1));
        Assert.That(all[1].Dependencies[0], Is.EqualTo("alpha >= 1.0.0"));
    }

    [Test]
    public void Add_ThenSaveAndReload_ExtensionPresent()
    {
        CreateExtensionOnDisk("test-ext", "1.2.3", "A test");

        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        registry.Save();

        var loaded = ExtensionRegistry.Load(_tempDir, null);

        Assert.That(loaded.Find("test-ext"), Is.Not.Null);
        Assert.That(loaded.Find("test-ext")!.Version, Is.EqualTo("1.2.3"));
    }

    [Test]
    public void Remove_ThenSaveAndReload_ExtensionAbsent()
    {
        CreateExtensionOnDisk("to-remove", "1.0.0", "Will be removed");

        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        registry.Save();

        // Now remove both from registry AND from disk
        var loaded = ExtensionRegistry.Load(_tempDir, null);
        var removed = loaded.Remove("to-remove");
        Directory.Delete(Path.Combine(_tempDir, "extensions", "to-remove"), recursive: true);
        loaded.Save();

        Assert.That(removed, Is.True);

        var reloaded = ExtensionRegistry.Load(_tempDir, null);
        Assert.That(reloaded.Find("to-remove"), Is.Null);
        Assert.That(reloaded.GetAll(), Has.Count.EqualTo(0));
    }

    [Test]
    public void Find_ExistingName_ReturnsExtension()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        registry.Add(new ExtensionInfo("findme", "3.0.0", "Find this",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "findme")),
            Array.Empty<string>()));

        var found = registry.Find("findme");

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Name, Is.EqualTo("findme"));
        Assert.That(found.Version, Is.EqualTo("3.0.0"));
    }

    [Test]
    public void Find_NonexistentName_ReturnsNull()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);

        Assert.That(registry.Find("nonexistent"), Is.Null);
    }

    [Test]
    public void Search_ByNameSubstring_ReturnsMatches()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        registry.Add(new ExtensionInfo("diagram-ext", "1.0.0", "Draws diagrams",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "diagram-ext")),
            Array.Empty<string>()));
        registry.Add(new ExtensionInfo("syntax-hl", "1.0.0", "Syntax highlighting",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "syntax-hl")),
            Array.Empty<string>()));

        var results = registry.Search("diagram");

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Name, Is.EqualTo("diagram-ext"));
    }

    [Test]
    public void Search_ByDescriptionSubstring_ReturnsMatches()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        registry.Add(new ExtensionInfo("my-ext", "1.0.0", "Draws fancy diagrams",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "my-ext")),
            Array.Empty<string>()));

        var results = registry.Search("diagrams");

        Assert.That(results, Has.Count.EqualTo(1));
        Assert.That(results[0].Name, Is.EqualTo("my-ext"));
    }

    [Test]
    public void Search_CaseInsensitive_ReturnsMatches()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        registry.Add(new ExtensionInfo("MyExtension", "1.0.0", "Some Description",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "MyExtension")),
            Array.Empty<string>()));

        var results = registry.Search("myextension");

        Assert.That(results, Has.Count.EqualTo(1));
    }

    [Test]
    public void Load_CorruptJson_RebuildsWithoutCrash()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "registry.json"), "{ this is not valid json }}}");

        var warnings = new List<string>();
        var registry = ExtensionRegistry.Load(_tempDir, msg => warnings.Add(msg));

        Assert.That(registry, Is.Not.Null);
        Assert.That(registry.GetAll(), Has.Count.EqualTo(0));
        Assert.That(warnings, Has.Count.GreaterThan(0));
    }

    [Test]
    public void Load_MissingRegistryJson_RebuildsFromFilesystem()
    {
        // Create an extension directory with a valid manifest
        var extDir = Path.Combine(_tempDir, "extensions", "test-ext");
        Directory.CreateDirectory(extDir);
        File.WriteAllText(Path.Combine(extDir, "extension.json"),
            """{"name": "test-ext", "version": "1.0.0", "entry": "Test.dll", "description": "Test extension"}""");

        // No registry.json exists — should rebuild from filesystem
        var registry = ExtensionRegistry.Load(_tempDir, null);
        var all = registry.GetAll();

        Assert.That(all, Has.Count.EqualTo(1));
        Assert.That(all[0].Name, Is.EqualTo("test-ext"));
        Assert.That(all[0].Version, Is.EqualTo("1.0.0"));
    }

    [Test]
    public void Load_WrongVersion_Rebuilds()
    {
        File.WriteAllText(Path.Combine(_tempDir, "registry.json"),
            """{"version": "999", "extensions": []}""");

        var warnings = new List<string>();
        var registry = ExtensionRegistry.Load(_tempDir, msg => warnings.Add(msg));

        Assert.That(registry, Is.Not.Null);
        Assert.That(warnings.Any(w => w.Contains("version mismatch")), Is.True);
    }

    [Test]
    public void GetAll_ReturnsSortedByName()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        registry.Add(new ExtensionInfo("zebra", "1.0.0", "",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "zebra")),
            Array.Empty<string>()));
        registry.Add(new ExtensionInfo("alpha", "1.0.0", "",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "alpha")),
            Array.Empty<string>()));

        var all = registry.GetAll();

        Assert.That(all[0].Name, Is.EqualTo("alpha"));
        Assert.That(all[1].Name, Is.EqualTo("zebra"));
    }

    [Test]
    public void Add_DuplicateName_ReplacesExisting()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        registry.Add(new ExtensionInfo("ext", "1.0.0", "Old",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "ext")),
            Array.Empty<string>()));
        registry.Add(new ExtensionInfo("ext", "2.0.0", "New",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "ext")),
            Array.Empty<string>()));

        var all = registry.GetAll();

        Assert.That(all, Has.Count.EqualTo(1));
        Assert.That(all[0].Version, Is.EqualTo("2.0.0"));
        Assert.That(all[0].Description, Is.EqualTo("New"));
    }

    [Test]
    public void Save_CreatesFileWithVersionField()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        registry.Save();

        var json = File.ReadAllText(Path.Combine(_tempDir, "registry.json"));

        Assert.That(json, Does.Contain("\"version\""));
        Assert.That(json, Does.Contain("\"1\""));
    }

    [Test]
    public void Rebuild_EmptyExtensionsDir_ReturnsEmptyRegistry()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "extensions"));

        var registry = ExtensionRegistry.Rebuild(_tempDir, null);

        Assert.That(registry.GetAll(), Has.Count.EqualTo(0));
    }

    [Test]
    public void Rebuild_NoExtensionsDir_ReturnsEmptyRegistry()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);

        Assert.That(registry.GetAll(), Has.Count.EqualTo(0));
    }

    private void CreateExtensionOnDisk(string name, string version, string description)
    {
        var extDir = Path.Combine(_tempDir, "extensions", name);
        Directory.CreateDirectory(extDir);
        File.WriteAllText(Path.Combine(extDir, "extension.json"),
            $"{{\"name\": \"{name}\", \"version\": \"{version}\", \"description\": \"{description}\", \"entry\": \"{name}.dll\"}}");
    }
}
