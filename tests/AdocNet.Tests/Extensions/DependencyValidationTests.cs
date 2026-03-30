using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class DependencyValidationTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"adocnet-depval-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void Manifest_WithDependenciesArray_ParsedCorrectly()
    {
        var json = """
            {
              "name": "my-ext",
              "entry": "My.dll",
              "dependencies": ["other >= 1.0.0", "utils >= 2.0.0"]
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/ext/my-ext", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.Dependencies, Has.Count.EqualTo(2));
        Assert.That(manifest.Dependencies[0], Is.EqualTo("other >= 1.0.0"));
        Assert.That(manifest.Dependencies[1], Is.EqualTo("utils >= 2.0.0"));
    }

    [Test]
    public void Manifest_WithDependenciesString_ParsedCorrectly()
    {
        var json = """
            {
              "name": "my-ext",
              "entry": "My.dll",
              "dependencies": "other >= 1.0.0, utils >= 2.0.0"
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/ext/my-ext", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.Dependencies, Has.Count.EqualTo(2));
        Assert.That(manifest.Dependencies[0], Is.EqualTo("other >= 1.0.0"));
        Assert.That(manifest.Dependencies[1], Is.EqualTo("utils >= 2.0.0"));
    }

    [Test]
    public void Manifest_NoDependencies_DefaultsToEmpty()
    {
        var json = """
            {
              "name": "my-ext",
              "entry": "My.dll"
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/ext/my-ext", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.Dependencies, Has.Count.EqualTo(0));
    }

    [Test]
    public void DependencySpec_ParseNameAndVersion_ReturnsSpec()
    {
        var spec = DependencySpec.Parse("other-ext >= 1.0.0");

        Assert.That(spec, Is.Not.Null);
        Assert.That(spec!.Name, Is.EqualTo("other-ext"));
        Assert.That(spec.MinVersion, Is.EqualTo("1.0.0"));
    }

    [Test]
    public void DependencySpec_ParseNameOnly_ReturnsSpecWithNullVersion()
    {
        var spec = DependencySpec.Parse("other-ext");

        Assert.That(spec, Is.Not.Null);
        Assert.That(spec!.Name, Is.EqualTo("other-ext"));
        Assert.That(spec.MinVersion, Is.Null);
    }

    [Test]
    public void DependencySpec_ParseEmpty_ReturnsNull()
    {
        Assert.That(DependencySpec.Parse(""), Is.Null);
        Assert.That(DependencySpec.Parse("  "), Is.Null);
    }

    [Test]
    public void Validate_DependencySatisfied_NoWarnings()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        registry.Add(new ExtensionInfo("dep-ext", "2.0.0", "Dependency",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "dep-ext")),
            Array.Empty<string>()));

        var ext = new ExtensionInfo("my-ext", "1.0.0", "My extension",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "my-ext")),
            new[] { "dep-ext >= 1.0.0" });

        var warnings = new List<string>();
        DependencyValidator.Validate(ext, registry, msg => warnings.Add(msg));

        Assert.That(warnings, Has.Count.EqualTo(0));
    }

    [Test]
    public void Validate_DependencyMissing_WarningProduced()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);

        var ext = new ExtensionInfo("my-ext", "1.0.0", "My extension",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "my-ext")),
            new[] { "missing-dep >= 1.0.0" });

        var warnings = new List<string>();
        DependencyValidator.Validate(ext, registry, msg => warnings.Add(msg));

        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("not installed"));
        Assert.That(warnings[0], Does.Contain("missing-dep"));
    }

    [Test]
    public void Validate_DependencyVersionTooLow_WarningProduced()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        registry.Add(new ExtensionInfo("dep-ext", "0.5.0", "Old dependency",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "dep-ext")),
            Array.Empty<string>()));

        var ext = new ExtensionInfo("my-ext", "1.0.0", "My extension",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "my-ext")),
            new[] { "dep-ext >= 1.0.0" });

        var warnings = new List<string>();
        DependencyValidator.Validate(ext, registry, msg => warnings.Add(msg));

        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("dep-ext"));
        Assert.That(warnings[0], Does.Contain("0.5.0"));
    }

    [Test]
    public void Validate_NoDependencies_NoWarnings()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);

        var ext = new ExtensionInfo("my-ext", "1.0.0", "My extension",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "my-ext")),
            Array.Empty<string>());

        var warnings = new List<string>();
        DependencyValidator.Validate(ext, registry, msg => warnings.Add(msg));

        Assert.That(warnings, Has.Count.EqualTo(0));
    }

    [Test]
    public void Validate_NameOnlyDependency_Satisfied_NoWarnings()
    {
        var registry = ExtensionRegistry.Rebuild(_tempDir, null);
        registry.Add(new ExtensionInfo("dep-ext", "0.1.0", "Any version",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "dep-ext")),
            Array.Empty<string>()));

        var ext = new ExtensionInfo("my-ext", "1.0.0", "My extension",
            Path.GetFullPath(Path.Combine(_tempDir, "extensions", "my-ext")),
            new[] { "dep-ext" });

        var warnings = new List<string>();
        DependencyValidator.Validate(ext, registry, msg => warnings.Add(msg));

        Assert.That(warnings, Has.Count.EqualTo(0));
    }
}
