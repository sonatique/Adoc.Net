using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class ExtensionManifestTests
{
    [Test]
    public void Parse_ValidJsonAllFields_ReturnsManifestWithAllProperties()
    {
        var json = """
            {
              "name": "my-extension",
              "version": "1.2.3",
              "description": "A test extension",
              "entry": "MyExtension.dll",
              "minAdocNetVersion": "1.0.0-beta.7"
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/ext/my-extension", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.Name, Is.EqualTo("my-extension"));
        Assert.That(manifest.Version, Is.EqualTo("1.2.3"));
        Assert.That(manifest.Description, Is.EqualTo("A test extension"));
        Assert.That(manifest.Entry, Is.EqualTo("MyExtension.dll"));
        Assert.That(manifest.MinAdocNetVersion, Is.EqualTo("1.0.0-beta.7"));
        Assert.That(manifest.DirectoryPath, Is.EqualTo("/ext/my-extension"));
    }

    [Test]
    public void Parse_OnlyRequiredFields_ReturnsManifestWithDefaults()
    {
        var json = """
            {
              "name": "minimal",
              "entry": "Minimal.dll"
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/ext/minimal", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.Name, Is.EqualTo("minimal"));
        Assert.That(manifest.Version, Is.EqualTo("0.0.0"));
        Assert.That(manifest.Description, Is.EqualTo(""));
        Assert.That(manifest.Entry, Is.EqualTo("Minimal.dll"));
        Assert.That(manifest.MinAdocNetVersion, Is.Null);
    }

    [Test]
    public void Parse_MissingName_ReturnsNullWithWarning()
    {
        var json = """
            {
              "entry": "MyExtension.dll"
            }
            """;
        var warnings = new List<string>();

        var manifest = ExtensionManifest.Parse(json, "/ext/bad", msg => warnings.Add(msg));

        Assert.That(manifest, Is.Null);
        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("missing required 'name' field"));
    }

    [Test]
    public void Parse_MissingEntry_ReturnsNullWithWarning()
    {
        var json = """
            {
              "name": "no-entry"
            }
            """;
        var warnings = new List<string>();

        var manifest = ExtensionManifest.Parse(json, "/ext/bad", msg => warnings.Add(msg));

        Assert.That(manifest, Is.Null);
        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("missing required 'entry' field"));
    }

    [Test]
    public void Parse_EmptyName_ReturnsNullWithWarning()
    {
        var json = """
            {
              "name": "  ",
              "entry": "MyExtension.dll"
            }
            """;
        var warnings = new List<string>();

        var manifest = ExtensionManifest.Parse(json, "/ext/bad", msg => warnings.Add(msg));

        Assert.That(manifest, Is.Null);
        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("missing required 'name' field"));
    }

    [Test]
    public void Parse_InvalidJson_ReturnsNullWithWarning()
    {
        var json = "{ this is not valid json }";
        var warnings = new List<string>();

        var manifest = ExtensionManifest.Parse(json, "/ext/bad", msg => warnings.Add(msg));

        Assert.That(manifest, Is.Null);
        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("invalid extension.json"));
    }

    [Test]
    public void Parse_EmptyJsonObject_ReturnsNullWithWarning()
    {
        var json = "{}";
        var warnings = new List<string>();

        var manifest = ExtensionManifest.Parse(json, "/ext/bad", msg => warnings.Add(msg));

        Assert.That(manifest, Is.Null);
        Assert.That(warnings, Has.Count.EqualTo(1));
    }

    [Test]
    public void Parse_NullJsonLiteral_ReturnsNullWithWarning()
    {
        var json = "null";
        var warnings = new List<string>();

        var manifest = ExtensionManifest.Parse(json, "/ext/bad", msg => warnings.Add(msg));

        Assert.That(manifest, Is.Null);
        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("invalid extension.json"));
    }

    [Test]
    public void Parse_UnknownFields_SilentlyIgnored()
    {
        var json = """
            {
              "name": "ext",
              "entry": "Ext.dll",
              "author": "someone",
              "license": "MIT"
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/ext/ext", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.Name, Is.EqualTo("ext"));
    }

    [Test]
    public void Parse_WhitespaceInValues_Trimmed()
    {
        var json = """
            {
              "name": "  my-ext  ",
              "entry": "  My.dll  ",
              "version": "  1.0.0  "
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/ext/my-ext", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.Name, Is.EqualTo("my-ext"));
        Assert.That(manifest.Entry, Is.EqualTo("My.dll"));
        Assert.That(manifest.Version, Is.EqualTo("1.0.0"));
    }

    [Test]
    public void Parse_EmptyMinAdocNetVersion_TreatedAsNull()
    {
        var json = """
            {
              "name": "ext",
              "entry": "Ext.dll",
              "minAdocNetVersion": ""
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/ext/ext", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.MinAdocNetVersion, Is.Null);
    }

    [Test]
    public void Load_MissingManifestFile_ReturnsNullWithWarning()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"adocnet-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var warnings = new List<string>();
            var manifest = ExtensionManifest.Load(tempDir, msg => warnings.Add(msg));

            Assert.That(manifest, Is.Null);
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("missing extension.json"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void Load_ValidManifestFile_ReturnsManifest()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"adocnet-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "extension.json"),
                """{"name": "test-ext", "entry": "Test.dll"}""");

            var manifest = ExtensionManifest.Load(tempDir, null);

            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest!.Name, Is.EqualTo("test-ext"));
            Assert.That(manifest.Entry, Is.EqualTo("Test.dll"));
            Assert.That(manifest.DirectoryPath, Is.EqualTo(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Test]
    public void Parse_NullJson_ThrowsArgumentNull()
    {
        Assert.That(() => ExtensionManifest.Parse(null!, "/ext", null),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Load_NullDirectory_ThrowsArgumentNull()
    {
        Assert.That(() => ExtensionManifest.Load(null!, null),
            Throws.TypeOf<ArgumentNullException>());
    }
}
