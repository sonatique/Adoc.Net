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
}
