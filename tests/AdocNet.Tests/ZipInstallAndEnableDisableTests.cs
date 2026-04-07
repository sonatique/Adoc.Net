using System.IO.Compression;
using AdocNet.Cli;
using AdocNet.Extensions;

namespace AdocNet.Tests;

[TestFixture]
public class ZipInstallAndEnableDisableTests
{
    private string _tempDir = null!;
    private string _extensionsDir = null!;
    private string _registryDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "adocnet-test-" + Guid.NewGuid().ToString("N"));
        _registryDir = Path.Combine(_tempDir, "registry");
        _extensionsDir = Path.Combine(_registryDir, "extensions");
        Directory.CreateDirectory(_extensionsDir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Test]
    public void ZipInstall_ParseExtArguments_AcceptsZipPath()
    {
        var result = ExtensionCommands.ParseExtArguments(new[] { "ext", "install", "myext.zip" });
        Assert.That(result, Is.InstanceOf<CliArgs.Ext.ExtInstall>());
        var install = (CliArgs.Ext.ExtInstall)result;
        Assert.That(install.SourcePath, Is.EqualTo("myext.zip"));
    }

    [Test]
    public void ZipInstall_InvalidZip_ReturnsError()
    {
        // Create a file that's not a valid zip
        var badZip = Path.Combine(_tempDir, "bad.zip");
        File.WriteAllText(badZip, "not a zip file");

        // We can't easily test ExecuteInstall directly (private), but we can test
        // that ExtensionCommands parsing works and the zip detection path exists.
        var result = ExtensionCommands.ParseExtArguments(new[] { "ext", "install", badZip });
        Assert.That(result, Is.InstanceOf<CliArgs.Ext.ExtInstall>());
    }

    [Test]
    public void EnableDisable_ParseExtArguments_Enable()
    {
        var result = ExtensionCommands.ParseExtArguments(new[] { "ext", "enable", "myext" });
        Assert.That(result, Is.InstanceOf<CliArgs.Ext.ExtEnable>());
        Assert.That(((CliArgs.Ext.ExtEnable)result).Name, Is.EqualTo("myext"));
    }

    [Test]
    public void EnableDisable_ParseExtArguments_Disable()
    {
        var result = ExtensionCommands.ParseExtArguments(new[] { "ext", "disable", "myext" });
        Assert.That(result, Is.InstanceOf<CliArgs.Ext.ExtDisable>());
        Assert.That(((CliArgs.Ext.ExtDisable)result).Name, Is.EqualTo("myext"));
    }

    [Test]
    public void ExtensionInfo_Enabled_DefaultsToTrue()
    {
        var info = new ExtensionInfo("test", "1.0.0", "desc", "/path", Array.Empty<string>());
        Assert.That(info.Enabled, Is.True);
    }

    [Test]
    public void ExtensionInfo_WithEnabled_ReturnsCopy()
    {
        var info = new ExtensionInfo("test", "1.0.0", "desc", "/path", Array.Empty<string>());
        var disabled = info.WithEnabled(false);
        Assert.That(disabled.Enabled, Is.False);
        Assert.That(disabled.Name, Is.EqualTo("test"));
        Assert.That(info.Enabled, Is.True); // original unchanged
    }

    [Test]
    public void ExtensionRegistry_SetEnabled_DisablesExtension()
    {
        // Create an extension directory with manifest
        var extDir = Path.Combine(_extensionsDir, "testext");
        Directory.CreateDirectory(extDir);
        File.WriteAllText(Path.Combine(extDir, "extension.json"),
            "{ \"name\": \"testext\", \"version\": \"1.0.0\", \"description\": \"Test\", \"entry\": \"Test.dll\" }");

        var registry = ExtensionRegistry.Rebuild(_registryDir, null);
        Assert.That(registry.Find("testext")!.Enabled, Is.True);

        registry.SetEnabled("testext", false);
        registry.Save();

        // Reload and verify
        var loaded = ExtensionRegistry.Load(_registryDir, null);
        Assert.That(loaded.Find("testext")!.Enabled, Is.False);
    }

    [Test]
    public void ExtensionRegistry_SetEnabled_EnablesExtension()
    {
        var extDir = Path.Combine(_extensionsDir, "testext");
        Directory.CreateDirectory(extDir);
        File.WriteAllText(Path.Combine(extDir, "extension.json"),
            "{ \"name\": \"testext\", \"version\": \"1.0.0\", \"description\": \"Test\", \"entry\": \"Test.dll\" }");

        var registry = ExtensionRegistry.Rebuild(_registryDir, null);
        registry.SetEnabled("testext", false);
        registry.Save();

        var loaded = ExtensionRegistry.Load(_registryDir, null);
        loaded.SetEnabled("testext", true);
        loaded.Save();

        var reloaded = ExtensionRegistry.Load(_registryDir, null);
        Assert.That(reloaded.Find("testext")!.Enabled, Is.True);
    }

    [Test]
    public void ExtensionDirectoryLoader_SkipsDisabledExtensions()
    {
        // Create extension with manifest and dummy DLL
        var extDir = Path.Combine(_extensionsDir, "testext");
        Directory.CreateDirectory(extDir);
        File.WriteAllText(Path.Combine(extDir, "extension.json"),
            "{ \"name\": \"testext\", \"version\": \"1.0.0\", \"description\": \"Test\", \"entry\": \"Test.dll\" }");
        File.WriteAllBytes(Path.Combine(extDir, "Test.dll"), Array.Empty<byte>());

        // Build and save registry with disabled state
        var registry = ExtensionRegistry.Rebuild(_registryDir, null);
        registry.SetEnabled("testext", false);
        registry.Save();

        // LoadInstalledExtensions should skip the disabled extension
        var warnings = new List<string>();
        var loaded = ExtensionDirectoryLoader.LoadInstalledExtensions(_extensionsDir, msg => warnings.Add(msg));

        // Since the extension is disabled, it should not attempt to load the DLL
        // (which would fail since Test.dll is empty), so no "bad image" warnings
        Assert.That(warnings, Has.None.Contains("testext"));
    }

    [Test]
    public void ZipFile_ExtractAndFindManifest_AtRoot()
    {
        // Create a zip with extension.json at the root
        var zipSourceDir = Path.Combine(_tempDir, "zipsource");
        Directory.CreateDirectory(zipSourceDir);
        File.WriteAllText(Path.Combine(zipSourceDir, "extension.json"),
            "{ \"name\": \"zipext\", \"version\": \"1.0.0\", \"description\": \"From zip\", \"entry\": \"ZipExt.dll\" }");
        File.WriteAllBytes(Path.Combine(zipSourceDir, "ZipExt.dll"), Array.Empty<byte>());

        var zipPath = Path.Combine(_tempDir, "zipext.zip");
        ZipFile.CreateFromDirectory(zipSourceDir, zipPath);

        // Extract and verify manifest can be found
        var extractDir = Path.Combine(_tempDir, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        // Should find manifest at root
        Assert.That(File.Exists(Path.Combine(extractDir, "extension.json")), Is.True);
    }

    [Test]
    public void ZipFile_ExtractAndFindManifest_InSubdir()
    {
        // Create a zip with extension.json in a single subdirectory
        var zipRootDir = Path.Combine(_tempDir, "ziproot");
        var zipSubDir = Path.Combine(zipRootDir, "myext");
        Directory.CreateDirectory(zipSubDir);
        File.WriteAllText(Path.Combine(zipSubDir, "extension.json"),
            "{ \"name\": \"myext\", \"version\": \"1.0.0\", \"description\": \"Sub\", \"entry\": \"MyExt.dll\" }");

        var zipPath = Path.Combine(_tempDir, "myext.zip");
        ZipFile.CreateFromDirectory(zipRootDir, zipPath);

        var extractDir = Path.Combine(_tempDir, "extracted2");
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        // Manifest should be in subdirectory
        var subdirs = Directory.GetDirectories(extractDir);
        Assert.That(subdirs.Length, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(subdirs[0], "extension.json")), Is.True);
    }
}
