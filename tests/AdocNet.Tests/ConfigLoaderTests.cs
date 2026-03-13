using AdocNet.Cli;

namespace AdocNet.Tests;

[TestFixture]
public class ConfigLoaderTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "adocnet-config-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Test]
    public void Discover_loads_config_from_directory()
    {
        File.WriteAllText(Path.Combine(_tempDir, "adocnet.json"),
            """{"format": "docbook", "recursive": true}""");

        var config = ConfigLoader.Discover(_tempDir);

        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Format, Is.EqualTo("docbook"));
        Assert.That(config.Recursive, Is.True);
    }

    [Test]
    public void Discover_returns_null_when_no_config_found()
    {
        var config = ConfigLoader.Discover(_tempDir);

        Assert.That(config, Is.Null);
    }

    [Test]
    public void Discover_walks_up_to_find_config()
    {
        File.WriteAllText(Path.Combine(_tempDir, "adocnet.json"),
            """{"outDir": "build"}""");

        var subDir = Path.Combine(_tempDir, "sub", "deep");
        Directory.CreateDirectory(subDir);

        var config = ConfigLoader.Discover(subDir);

        Assert.That(config, Is.Not.Null);
        Assert.That(config!.OutDir, Is.EqualTo("build"));
    }

    [Test]
    public void Discover_loads_attributes()
    {
        File.WriteAllText(Path.Combine(_tempDir, "adocnet.json"),
            """{"attributes": {"author": "Test", "version": "1.0"}}""");

        var config = ConfigLoader.Discover(_tempDir);

        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Attributes, Is.Not.Null);
        Assert.That(config.Attributes!["author"], Is.EqualTo("Test"));
        Assert.That(config.Attributes["version"], Is.EqualTo("1.0"));
    }

    [Test]
    public void LoadFrom_loads_from_explicit_path()
    {
        var path = Path.Combine(_tempDir, "custom.json");
        File.WriteAllText(path, """{"styled": true, "theme": "clean"}""");

        var config = ConfigLoader.LoadFrom(path);

        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Styled, Is.True);
        Assert.That(config.Theme, Is.EqualTo("clean"));
    }

    [Test]
    public void LoadFrom_returns_null_for_invalid_json()
    {
        var path = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(path, "not valid json {{{");

        var config = ConfigLoader.LoadFrom(path);

        Assert.That(config, Is.Null);
    }

    [Test]
    public void LoadFrom_ignores_unknown_fields()
    {
        var path = Path.Combine(_tempDir, "extra.json");
        File.WriteAllText(path, """{"format": "html", "unknownField": 42, "anotherOne": "test"}""");

        var config = ConfigLoader.LoadFrom(path);

        Assert.That(config, Is.Not.Null);
        Assert.That(config!.Format, Is.EqualTo("html"));
    }
}
