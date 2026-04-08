using AdocNet.Extensions;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class SigningVerificationTests
{
    [Test]
    public void ManifestWithPublicKeyToken_ParsedCorrectly()
    {
        var json = """
            {
                "name": "test-signed",
                "version": "1.0.0",
                "entry": "TestSigned.dll",
                "publicKeyToken": "ab40020b151f4aae"
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/tmp/test", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.PublicKeyToken, Is.EqualTo("ab40020b151f4aae"));
    }

    [Test]
    public void ManifestWithoutPublicKeyToken_TokenIsNull()
    {
        var json = """
            {
                "name": "test-unsigned",
                "version": "1.0.0",
                "entry": "TestUnsigned.dll"
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/tmp/test", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.PublicKeyToken, Is.Null);
    }

    [Test]
    public void ManifestWithInvalidTokenFormat_TokenIgnored()
    {
        var warnings = new List<string>();
        var json = """
            {
                "name": "test-bad-token",
                "version": "1.0.0",
                "entry": "Test.dll",
                "publicKeyToken": "not-a-hex"
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/tmp/test", msg => warnings.Add(msg));

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.PublicKeyToken, Is.Null);
        Assert.That(warnings, Has.Some.Contains("invalid publicKeyToken format"));
    }

    [Test]
    public void ManifestWithUpperCaseToken_NormalizedToLowerCase()
    {
        var json = """
            {
                "name": "test-upper",
                "version": "1.0.0",
                "entry": "Test.dll",
                "publicKeyToken": "AB40020B151F4AAE"
            }
            """;

        var manifest = ExtensionManifest.Parse(json, "/tmp/test", null);

        Assert.That(manifest, Is.Not.Null);
        Assert.That(manifest!.PublicKeyToken, Is.EqualTo("ab40020b151f4aae"));
    }

    [Test]
    public void SignedDll_MatchingToken_LoadsNormally()
    {
        var signedDllPath = GetSignedExtensionDllPath();
        if (signedDllPath is null)
        {
            Assert.Ignore("Signed test extension DLL not found (build AdocNet.TestSignedExtension first).");
            return;
        }

        // Get actual token from the DLL
        var assemblyName = System.Reflection.AssemblyName.GetAssemblyName(signedDllPath);
        var actualToken = SigningHelper.ToHexString(assemblyName.GetPublicKeyToken());
        Assert.That(actualToken, Is.Not.Empty, "Test DLL should be signed");

        // Create a temp extension directory with matching manifest
        var tempDir = Path.Combine(Path.GetTempPath(), "adocnet-sign-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var dllDest = Path.Combine(tempDir, "AdocNet.TestSignedExtension.dll");
            File.Copy(signedDllPath, dllDest);

            var json = $$"""
                {
                    "name": "test-signed",
                    "version": "1.0.0",
                    "entry": "AdocNet.TestSignedExtension.dll",
                    "publicKeyToken": "{{actualToken}}"
                }
                """;
            File.WriteAllText(Path.Combine(tempDir, "extension.json"), json);

            // Load via directory loader (needs a parent dir with subdirs)
            var parentDir = Path.Combine(Path.GetTempPath(), "adocnet-sign-parent-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(parentDir);
            var extDir = Path.Combine(parentDir, "test-signed");
            Directory.CreateDirectory(extDir);
            foreach (var file in Directory.GetFiles(tempDir))
                File.Copy(file, Path.Combine(extDir, Path.GetFileName(file)));

            var warnings = new List<string>();
            var result = ExtensionDirectoryLoader.LoadInstalledExtensions(parentDir, msg => warnings.Add(msg));

            // Should load without token mismatch warnings
            Assert.That(warnings, Has.None.Contains("publicKeyToken mismatch"));
            Assert.That(warnings, Has.None.Contains("unsigned"));

            try { Directory.Delete(parentDir, true); } catch { /* DLL may be locked */ }
        }
        finally
        {
            if (Directory.Exists(tempDir))
                try { Directory.Delete(tempDir, true); } catch { /* DLL may be locked */ }
        }
    }

    [Test]
    public void SignedDll_WrongToken_SkippedWithWarning()
    {
        var signedDllPath = GetSignedExtensionDllPath();
        if (signedDllPath is null)
        {
            Assert.Ignore("Signed test extension DLL not found.");
            return;
        }

        var parentDir = Path.Combine(Path.GetTempPath(), "adocnet-sign-wrong-" + Guid.NewGuid().ToString("N"));
        try
        {
            var extDir = Path.Combine(parentDir, "test-wrong");
            Directory.CreateDirectory(extDir);
            File.Copy(signedDllPath, Path.Combine(extDir, "AdocNet.TestSignedExtension.dll"));

            var json = """
                {
                    "name": "test-wrong",
                    "version": "1.0.0",
                    "entry": "AdocNet.TestSignedExtension.dll",
                    "publicKeyToken": "0000000000000000"
                }
                """;
            File.WriteAllText(Path.Combine(extDir, "extension.json"), json);

            var warnings = new List<string>();
            var result = ExtensionDirectoryLoader.LoadInstalledExtensions(parentDir, msg => warnings.Add(msg));

            Assert.That(warnings, Has.Some.Contains("publicKeyToken mismatch"));
        }
        finally
        {
            if (Directory.Exists(parentDir))
                try { Directory.Delete(parentDir, true); } catch { /* DLL may be locked */ }
        }
    }

    [Test]
    public void UnsignedDll_ManifestExpectsToken_SkippedWithWarning()
    {
        // Use the regular (unsigned) test extension
        var unsignedDllPath = GetUnsignedExtensionDllPath();
        if (unsignedDllPath is null)
        {
            Assert.Ignore("Unsigned test extension DLL not found.");
            return;
        }

        var parentDir = Path.Combine(Path.GetTempPath(), "adocnet-sign-unsigned-" + Guid.NewGuid().ToString("N"));
        try
        {
            var extDir = Path.Combine(parentDir, "test-unsigned");
            Directory.CreateDirectory(extDir);
            File.Copy(unsignedDllPath, Path.Combine(extDir, "AdocNet.TestExtension.dll"));

            var json = """
                {
                    "name": "test-unsigned",
                    "version": "1.0.0",
                    "entry": "AdocNet.TestExtension.dll",
                    "publicKeyToken": "ab40020b151f4aae"
                }
                """;
            File.WriteAllText(Path.Combine(extDir, "extension.json"), json);

            var warnings = new List<string>();
            var result = ExtensionDirectoryLoader.LoadInstalledExtensions(parentDir, msg => warnings.Add(msg));

            Assert.That(warnings, Has.Some.Contains("unsigned"));
        }
        finally
        {
            if (Directory.Exists(parentDir))
                try { Directory.Delete(parentDir, true); } catch { /* DLL may be locked */ }
        }
    }

    [Test]
    public void NoTokenInManifest_LoadsNormally()
    {
        var unsignedDllPath = GetUnsignedExtensionDllPath();
        if (unsignedDllPath is null)
        {
            Assert.Ignore("Test extension DLL not found.");
            return;
        }

        var parentDir = Path.Combine(Path.GetTempPath(), "adocnet-sign-notoken-" + Guid.NewGuid().ToString("N"));
        try
        {
            var extDir = Path.Combine(parentDir, "test-notoken");
            Directory.CreateDirectory(extDir);
            File.Copy(unsignedDllPath, Path.Combine(extDir, "AdocNet.TestExtension.dll"));

            // Copy dependencies
            var srcDir = Path.GetDirectoryName(unsignedDllPath)!;
            foreach (var dep in new[] { "AdocNet.Ast.dll", "AdocNet.Core.dll" })
            {
                var depPath = Path.Combine(srcDir, dep);
                if (File.Exists(depPath))
                    File.Copy(depPath, Path.Combine(extDir, dep));
            }

            var json = """
                {
                    "name": "test-notoken",
                    "version": "1.0.0",
                    "entry": "AdocNet.TestExtension.dll"
                }
                """;
            File.WriteAllText(Path.Combine(extDir, "extension.json"), json);

            var warnings = new List<string>();
            var result = ExtensionDirectoryLoader.LoadInstalledExtensions(parentDir, msg => warnings.Add(msg));

            // No signing-related warnings
            Assert.That(warnings, Has.None.Contains("publicKeyToken"));
            Assert.That(warnings, Has.None.Contains("unsigned"));
        }
        finally
        {
            if (Directory.Exists(parentDir))
                try { Directory.Delete(parentDir, true); } catch { /* DLL may be locked */ }
        }
    }

    private static string? GetSignedExtensionDllPath()
    {
        var testDir = Path.GetDirectoryName(typeof(SigningVerificationTests).Assembly.Location)!;
        var configDir = Path.GetDirectoryName(testDir)!;
        var config = Path.GetFileName(configDir);
        var extensionDir = Path.Combine(testDir, "..", "..", "..", "..",
            "AdocNet.TestSignedExtension", "bin", config!, "net10.0");
        var path = Path.GetFullPath(Path.Combine(extensionDir, "AdocNet.TestSignedExtension.dll"));
        return File.Exists(path) ? path : null;
    }

    private static string? GetUnsignedExtensionDllPath()
    {
        var testDir = Path.GetDirectoryName(typeof(SigningVerificationTests).Assembly.Location)!;
        var configDir = Path.GetDirectoryName(testDir)!;
        var config = Path.GetFileName(configDir);
        var extensionDir = Path.Combine(testDir, "..", "..", "..", "..",
            "AdocNet.TestExtension", "bin", config!, "net10.0");
        var path = Path.GetFullPath(Path.Combine(extensionDir, "AdocNet.TestExtension.dll"));
        return File.Exists(path) ? path : null;
    }
}
