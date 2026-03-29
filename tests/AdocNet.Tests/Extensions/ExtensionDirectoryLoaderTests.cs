using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class ExtensionDirectoryLoaderTests
{
    private string _tempRoot = null!;

    [SetUp]
    public void SetUp()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"adocnet-dirloader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Test]
    public void LoadInstalledExtensions_NonexistentDirectory_ReturnsEmpty()
    {
        var nonexistent = Path.Combine(_tempRoot, "does-not-exist");

        var results = ExtensionDirectoryLoader.LoadInstalledExtensions(nonexistent, null);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void LoadInstalledExtensions_EmptyDirectory_ReturnsEmpty()
    {
        var results = ExtensionDirectoryLoader.LoadInstalledExtensions(_tempRoot, null);

        Assert.That(results, Is.Empty);
    }

    [Test]
    public void LoadInstalledExtensions_MissingManifest_SkipsWithWarning()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "bad-ext"));
        var warnings = new List<string>();

        var results = ExtensionDirectoryLoader.LoadInstalledExtensions(_tempRoot, msg => warnings.Add(msg));

        Assert.That(results, Is.Empty);
        Assert.That(warnings, Has.Count.EqualTo(1));
        Assert.That(warnings[0], Does.Contain("missing extension.json"));
    }

    [Test]
    public void LoadInstalledExtensions_EntryDllNotFound_SkipsWithWarning()
    {
        var extDir = Path.Combine(_tempRoot, "missing-dll");
        Directory.CreateDirectory(extDir);
        File.WriteAllText(Path.Combine(extDir, "extension.json"),
            """{"name": "missing-dll", "entry": "NonExistent.dll"}""");

        var warnings = new List<string>();
        var results = ExtensionDirectoryLoader.LoadInstalledExtensions(_tempRoot, msg => warnings.Add(msg));

        Assert.That(results, Is.Empty);
        Assert.That(warnings, Has.Exactly(1).Matches<string>(w => w.Contains("entry DLL not found")));
    }

    [Test]
    public void LoadInstalledExtensions_ValidExtension_LoadsProcessors()
    {
        // Use the real TestExtension DLL
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

        // Copy the DLL and its dependencies
        CopyExtensionFiles(testExtDll, extDir);

        var warnings = new List<string>();
        var results = ExtensionDirectoryLoader.LoadInstalledExtensions(_tempRoot, msg => warnings.Add(msg));

        Assert.That(results, Has.Count.GreaterThanOrEqualTo(1),
            "Should load at least one processor from TestExtension");
    }

    [Test]
    public void LoadInstalledExtensions_FoldersSortedAlphabetically()
    {
        // Create folders in reverse order
        var folderC = Path.Combine(_tempRoot, "zzz-ext");
        var folderA = Path.Combine(_tempRoot, "aaa-ext");
        var folderB = Path.Combine(_tempRoot, "mmm-ext");

        Directory.CreateDirectory(folderC);
        Directory.CreateDirectory(folderA);
        Directory.CreateDirectory(folderB);

        // Only aaa-ext and mmm-ext get valid manifests (zzz-ext has no manifest)
        File.WriteAllText(Path.Combine(folderC, "extension.json"),
            """{"name": "zzz-ext", "entry": "Missing.dll"}""");
        File.WriteAllText(Path.Combine(folderA, "extension.json"),
            """{"name": "aaa-ext", "entry": "Missing.dll"}""");
        File.WriteAllText(Path.Combine(folderB, "extension.json"),
            """{"name": "mmm-ext", "entry": "Missing.dll"}""");

        var warnings = new List<string>();
        ExtensionDirectoryLoader.LoadInstalledExtensions(_tempRoot, msg => warnings.Add(msg));

        // All three should produce "entry DLL not found" warnings in alphabetical order
        var dllWarnings = warnings.Where(w => w.Contains("entry DLL not found")).ToList();
        Assert.That(dllWarnings, Has.Count.EqualTo(3));
        Assert.That(dllWarnings[0], Does.Contain("aaa-ext"));
        Assert.That(dllWarnings[1], Does.Contain("mmm-ext"));
        Assert.That(dllWarnings[2], Does.Contain("zzz-ext"));
    }

    [Test]
    public void LoadInstalledExtensions_IncompatibleVersion_SkipsWithWarning()
    {
        var extDir = Path.Combine(_tempRoot, "future-ext");
        Directory.CreateDirectory(extDir);
        File.WriteAllText(Path.Combine(extDir, "extension.json"),
            """{"name": "future-ext", "entry": "Future.dll", "minAdocNetVersion": "99.0.0"}""");
        // Create a dummy DLL so we get past the DLL-exists check
        File.WriteAllText(Path.Combine(extDir, "Future.dll"), "dummy");

        var warnings = new List<string>();
        var results = ExtensionDirectoryLoader.LoadInstalledExtensions(_tempRoot, msg => warnings.Add(msg));

        Assert.That(results, Is.Empty);
        Assert.That(warnings, Has.Exactly(1).Matches<string>(w =>
            w.Contains("requires AdocNet >=") && w.Contains("99.0.0")));
    }

    [Test]
    public void LoadInstalledExtensions_NullDirectory_UsesDefault()
    {
        // Just verify it doesn't throw — the default directory likely doesn't exist
        var results = ExtensionDirectoryLoader.LoadInstalledExtensions(null, null);
        Assert.That(results, Is.Not.Null);
    }

    [Test]
    public void GetDefaultExtensionDirectory_ReturnsPathWithAdocnetExtensions()
    {
        var path = ExtensionDirectoryLoader.GetDefaultExtensionDirectory();

        Assert.That(path, Does.Contain(".adocnet"));
        Assert.That(path, Does.EndWith("extensions"));
    }

    // --- Version Compatibility Tests ---

    [Test]
    public void IsVersionCompatible_EqualVersions_ReturnsTrue()
    {
        Assert.That(ExtensionDirectoryLoader.IsVersionCompatible("1.0.0-beta.7", "1.0.0-beta.7"), Is.True);
    }

    [Test]
    public void IsVersionCompatible_NewerVersion_ReturnsTrue()
    {
        Assert.That(ExtensionDirectoryLoader.IsVersionCompatible("1.0.0-beta.8", "1.0.0-beta.7"), Is.True);
    }

    [Test]
    public void IsVersionCompatible_OlderVersion_ReturnsFalse()
    {
        Assert.That(ExtensionDirectoryLoader.IsVersionCompatible("1.0.0-beta.6", "1.0.0-beta.7"), Is.False);
    }

    [Test]
    public void IsVersionCompatible_ReleaseNewerThanPrerelease_ReturnsTrue()
    {
        Assert.That(ExtensionDirectoryLoader.IsVersionCompatible("1.0.0", "1.0.0-beta.7"), Is.True);
    }

    [Test]
    public void IsVersionCompatible_PrereleaseOlderThanRelease_ReturnsFalse()
    {
        Assert.That(ExtensionDirectoryLoader.IsVersionCompatible("1.0.0-beta.7", "1.0.0"), Is.False);
    }

    [Test]
    public void IsVersionCompatible_NullMinimum_ReturnsTrue()
    {
        Assert.That(ExtensionDirectoryLoader.IsVersionCompatible("1.0.0", null!), Is.True);
    }

    [Test]
    public void IsVersionCompatible_EmptyMinimum_ReturnsTrue()
    {
        Assert.That(ExtensionDirectoryLoader.IsVersionCompatible("1.0.0", ""), Is.True);
    }

    [Test]
    public void IsVersionCompatible_HigherMajorVersion_ReturnsTrue()
    {
        Assert.That(ExtensionDirectoryLoader.IsVersionCompatible("2.0.0", "1.0.0-beta.7"), Is.True);
    }

    [Test]
    public void IsVersionCompatible_LowerMajorVersion_ReturnsFalse()
    {
        Assert.That(ExtensionDirectoryLoader.IsVersionCompatible("0.9.0", "1.0.0"), Is.False);
    }

    [Test]
    public void GetCurrentAdocNetVersion_ReturnsNonEmptyString()
    {
        var version = ExtensionDirectoryLoader.GetCurrentAdocNetVersion();
        Assert.That(version, Is.Not.Null.And.Not.Empty);
    }

    // --- Helpers ---

    private static string? GetTestExtensionDllPath()
    {
        // Look for the TestExtension DLL in known build output locations
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
        {
            File.Copy(file, Path.Combine(targetDir, Path.GetFileName(file)), overwrite: true);
        }
    }
}
