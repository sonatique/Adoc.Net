using AdocNet.Extensions;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class ExtensionValidatorTests
{
    [Test]
    public void Validate_ValidExtension_AllChecksPass()
    {
        var dllPath = GetTestExtensionDllPath();
        if (dllPath is null)
        {
            Assert.Ignore("Test extension DLL not found.");
            return;
        }

        var tempDir = CreateTempExtensionDir("valid-ext", dllPath, new Dictionary<string, string>
        {
            ["name"] = "valid-ext",
            ["version"] = "1.0.0",
            ["entry"] = "AdocNet.TestExtension.dll"
        });

        try
        {
            var validator = new ExtensionValidator();
            var results = validator.Validate(tempDir);

            var failCount = CountByStatus(results, ValidationStatus.Fail);
            Assert.That(failCount, Is.EqualTo(0), FormatResults(results));

            var passCount = CountByStatus(results, ValidationStatus.Pass);
            Assert.That(passCount, Is.GreaterThanOrEqualTo(4));
        }
        finally
        {
            TryDelete(tempDir);
        }
    }

    [Test]
    public void Validate_MissingManifest_FirstCheckFails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-val-nomanifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var validator = new ExtensionValidator();
            var results = validator.Validate(tempDir);

            Assert.That(results, Has.Count.EqualTo(1));
            Assert.That(results[0].Status, Is.EqualTo(ValidationStatus.Fail));
            Assert.That(results[0].CheckName, Is.EqualTo("Manifest"));
        }
        finally
        {
            TryDelete(tempDir);
        }
    }

    [Test]
    public void Validate_MissingEntryDll_EntryCheckFails()
    {
        var tempDir = CreateTempExtensionDirNoFiles("missing-dll", new Dictionary<string, string>
        {
            ["name"] = "missing-dll",
            ["version"] = "1.0.0",
            ["entry"] = "NonExistent.dll"
        });

        try
        {
            var validator = new ExtensionValidator();
            var results = validator.Validate(tempDir);

            var entryResult = results.FirstOrDefault(r => r.CheckName == "Entry DLL");
            Assert.That(entryResult, Is.Not.Null);
            Assert.That(entryResult!.Status, Is.EqualTo(ValidationStatus.Fail));
        }
        finally
        {
            TryDelete(tempDir);
        }
    }

    [Test]
    public void Validate_InvalidDll_LoadCheckFails()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-val-baddll-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write a non-DLL file as the entry
            File.WriteAllText(Path.Combine(tempDir, "Bad.dll"), "not a dll");
            WriteManifest(tempDir, new Dictionary<string, string>
            {
                ["name"] = "bad-dll",
                ["version"] = "1.0.0",
                ["entry"] = "Bad.dll"
            });

            var validator = new ExtensionValidator();
            var results = validator.Validate(tempDir);

            var loadResult = results.FirstOrDefault(r => r.CheckName == "DLL loading");
            Assert.That(loadResult, Is.Not.Null);
            Assert.That(loadResult!.Status, Is.EqualTo(ValidationStatus.Fail));
        }
        finally
        {
            TryDelete(tempDir);
        }
    }

    [Test]
    public void Validate_IncompatibleApiVersion_ApiFails()
    {
        var dllPath = GetTestExtensionDllPath();
        if (dllPath is null)
        {
            Assert.Ignore("Test extension DLL not found.");
            return;
        }

        var tempDir = CreateTempExtensionDir("api-incompat", dllPath, new Dictionary<string, string>
        {
            ["name"] = "api-incompat",
            ["version"] = "1.0.0",
            ["entry"] = "AdocNet.TestExtension.dll",
            ["apiVersion"] = "99.0" // Way beyond any real version
        });

        try
        {
            var validator = new ExtensionValidator();
            var results = validator.Validate(tempDir);

            var apiResult = results.FirstOrDefault(r => r.CheckName == "API version");
            Assert.That(apiResult, Is.Not.Null);
            Assert.That(apiResult!.Status, Is.EqualTo(ValidationStatus.Fail));
        }
        finally
        {
            TryDelete(tempDir);
        }
    }

    [Test]
    public void Validate_NoProcessors_ProcessorCheckFails()
    {
        var dllPath = GetEmptyExtensionDllPath();
        if (dllPath is null)
        {
            Assert.Ignore("Empty test extension DLL not found.");
            return;
        }

        var tempDir = CreateTempExtensionDir("no-processors", dllPath, new Dictionary<string, string>
        {
            ["name"] = "no-processors",
            ["version"] = "1.0.0",
            ["entry"] = Path.GetFileName(dllPath)
        });

        try
        {
            var validator = new ExtensionValidator();
            var results = validator.Validate(tempDir);

            var procResult = results.FirstOrDefault(r => r.CheckName == "Processors");
            Assert.That(procResult, Is.Not.Null);
            Assert.That(procResult!.Status, Is.EqualTo(ValidationStatus.Fail));
        }
        finally
        {
            TryDelete(tempDir);
        }
    }

    [Test]
    public void Validate_NullPath_Throws()
    {
        var validator = new ExtensionValidator();
        Assert.Throws<ArgumentNullException>(() => validator.Validate(null!));
    }

    // --- Helpers ---

    private static int CountByStatus(IReadOnlyList<ValidationResult> results, ValidationStatus status)
    {
        int count = 0;
        foreach (var r in results)
            if (r.Status == status) count++;
        return count;
    }

    private static string FormatResults(IReadOnlyList<ValidationResult> results)
    {
        var lines = new List<string>();
        foreach (var r in results)
            lines.Add($"[{r.Status}] {r.CheckName}: {r.Message}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string CreateTempExtensionDir(string name, string dllPath,
        Dictionary<string, string> manifestFields)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"adocnet-val-{name}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        File.Copy(dllPath, Path.Combine(tempDir, Path.GetFileName(dllPath)));

        // Copy dependency DLLs
        var srcDir = Path.GetDirectoryName(dllPath)!;
        foreach (var dep in new[] { "AdocNet.Ast.dll", "AdocNet.Core.dll" })
        {
            var depPath = Path.Combine(srcDir, dep);
            if (File.Exists(depPath))
                File.Copy(depPath, Path.Combine(tempDir, dep));
        }

        WriteManifest(tempDir, manifestFields);
        return tempDir;
    }

    private static string CreateTempExtensionDirNoFiles(string name,
        Dictionary<string, string> manifestFields)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"adocnet-val-{name}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        WriteManifest(tempDir, manifestFields);
        return tempDir;
    }

    private static void WriteManifest(string dir, Dictionary<string, string> fields)
    {
        var entries = new List<string>();
        foreach (var kvp in fields)
            entries.Add($"  \"{kvp.Key}\": \"{kvp.Value}\"");
        var json = "{\n" + string.Join(",\n", entries) + "\n}";
        File.WriteAllText(Path.Combine(dir, "extension.json"), json);
    }

    private static void TryDelete(string path)
    {
        try { Directory.Delete(path, true); } catch { /* DLL may be locked */ }
    }

    private static string? GetTestExtensionDllPath()
    {
        var testDir = Path.GetDirectoryName(typeof(ExtensionValidatorTests).Assembly.Location)!;
        var configDir = Path.GetDirectoryName(testDir)!;
        var config = Path.GetFileName(configDir);
        var extensionDir = Path.Combine(testDir, "..", "..", "..", "..",
            "AdocNet.TestExtension", "bin", config!, "net10.0");
        var path = Path.GetFullPath(Path.Combine(extensionDir, "AdocNet.TestExtension.dll"));
        return File.Exists(path) ? path : null;
    }

    private static string? GetEmptyExtensionDllPath()
    {
        var testDir = Path.GetDirectoryName(typeof(ExtensionValidatorTests).Assembly.Location)!;
        var configDir = Path.GetDirectoryName(testDir)!;
        var config = Path.GetFileName(configDir);
        var extensionDir = Path.Combine(testDir, "..", "..", "..", "..",
            "AdocNet.TestEmptyExtension", "bin", config!, "net10.0");
        var path = Path.GetFullPath(Path.Combine(extensionDir, "AdocNet.TestEmptyExtension.dll"));
        return File.Exists(path) ? path : null;
    }
}
