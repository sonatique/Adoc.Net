using AdocNet.Ast;
using AdocNet.Extensions;
using NUnit.Framework;

namespace AdocNet.Tests.Extensions;

[TestFixture]
public class HotReloadTests
{
    private sealed class StubRenderer : IDocumentRenderer
    {
        public string Format => "stub";
        public void Render(DocumentNode document, Stream output, RenderOptions options) { }
    }

#if NET6_0_OR_GREATER
    [Test]
    public void EnableHotReload_True_NoError()
    {
        var engine = new AdocEngine(new StubRenderer(), _ => new DocumentNode());
        Assert.DoesNotThrow(() => engine.EnableHotReload = true);
        Assert.That(engine.EnableHotReload, Is.True);
        engine.Shutdown();
    }

    [Test]
    public void EnableHotReload_SetFalse_StopsWatchers()
    {
        var engine = new AdocEngine(new StubRenderer(), _ => new DocumentNode());
        engine.EnableHotReload = true;
        engine.EnableHotReload = false;
        Assert.That(engine.EnableHotReload, Is.False);
        engine.Shutdown();
    }

    [Test]
    public void DllChange_TriggersReload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"adocnet-hotreload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Copy the test extension DLL to the temp directory
            var srcDll = GetTestExtensionDllPath();
            if (srcDll is null)
                Assert.Ignore("TestExtension DLL not found");

            var destDll = Path.Combine(tempDir, "AdocNet.TestExtension.dll");
            File.Copy(srcDll, destDll);

            // Copy dependencies too
            var srcDir = Path.GetDirectoryName(srcDll)!;
            foreach (var dep in Directory.GetFiles(srcDir, "*.dll"))
            {
                var depDest = Path.Combine(tempDir, Path.GetFileName(dep));
                if (!File.Exists(depDest))
                    File.Copy(dep, depDest);
            }

            var warnings = new List<string>();
            var engine = new AdocEngine(new StubRenderer(), _ => new DocumentNode());
            engine.OnWarning = msg => warnings.Add(msg);
            engine.EnableHotReload = true;
            engine.LoadExtensions(tempDir);

            // Trigger a Convert so processors are frozen
            engine.Convert("test", new MemoryStream());

            // Simulate DLL change by touching the file
            var reloadEvent = new ManualResetEventSlim(false);
            engine.OnWarning = msg =>
            {
                warnings.Add(msg);
                if (msg.Contains("Hot-reload"))
                    reloadEvent.Set();
            };

            // Touch the DLL to trigger FileSystemWatcher
            File.SetLastWriteTimeUtc(destDll, DateTime.UtcNow);

            // Wait for reload (debounce 500ms + processing time)
            var reloaded = reloadEvent.Wait(TimeSpan.FromSeconds(5));
            Assert.That(reloaded, Is.True, "Reload should have been triggered");
            Assert.That(warnings, Has.Some.Contain("Hot-reload"));

            engine.Shutdown();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Test]
    public void CacheCleared_OnReload()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"adocnet-hotreload-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Use no-extension directory so caching works (extensions disable parse cache)
            int parseCount = 0;
            var engine = new AdocEngine(new StubRenderer(), s =>
            {
                Interlocked.Increment(ref parseCount);
                return new DocumentNode();
            });
            engine.EnableCaching = true;
            engine.EnableHotReload = true;

            // Load from empty dir — no extensions, so caching works
            // Create a dummy DLL so LoadExtensions doesn't warn about empty dir
            var dummyDll = Path.Combine(tempDir, "dummy.txt");
            File.WriteAllText(dummyDll, "placeholder");

            // First convert populates cache
            engine.Convert("test", new MemoryStream());
            var countAfterFirst = parseCount;

            // Second convert should use cache (no re-parse)
            engine.Convert("test", new MemoryStream());
            Assert.That(parseCount, Is.EqualTo(countAfterFirst), "Second call should use cache");

            // Directly call ReloadExtensions to simulate a hot-reload which clears cache
            engine.ReloadExtensions(tempDir);

            // After reload, cache should be cleared — next convert re-parses
            engine.Convert("test", new MemoryStream());
            Assert.That(parseCount, Is.GreaterThan(countAfterFirst), "Cache should be cleared after reload");

            engine.Shutdown();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    [Test]
    public void Shutdown_StopsWatcher_NoMoreReloads()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"adocnet-hotreload-stop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var srcDll = GetTestExtensionDllPath();
            if (srcDll is null)
                Assert.Ignore("TestExtension DLL not found");

            var destDll = Path.Combine(tempDir, "AdocNet.TestExtension.dll");
            File.Copy(srcDll, destDll);

            var srcDir = Path.GetDirectoryName(srcDll)!;
            foreach (var dep in Directory.GetFiles(srcDir, "*.dll"))
            {
                var depDest = Path.Combine(tempDir, Path.GetFileName(dep));
                if (!File.Exists(depDest))
                    File.Copy(dep, depDest);
            }

            var warnings = new List<string>();
            var engine = new AdocEngine(new StubRenderer(), _ => new DocumentNode());
            engine.OnWarning = msg => warnings.Add(msg);
            engine.EnableHotReload = true;
            engine.LoadExtensions(tempDir);
            engine.Convert("test", new MemoryStream());

            // Shutdown stops watchers
            engine.Shutdown();

            // Touch DLL after shutdown — should NOT trigger reload
            warnings.Clear();
            File.SetLastWriteTimeUtc(destDll, DateTime.UtcNow);
            Thread.Sleep(1500); // Wait well past debounce

            Assert.That(warnings, Has.None.Contain("Hot-reload"),
                "No reload should occur after Shutdown");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
#else
    [Test]
    public void EnableHotReload_True_ThrowsOnNetStandard()
    {
        var engine = new AdocEngine(new StubRenderer(), _ => new DocumentNode());
        Assert.Throws<NotSupportedException>(() => engine.EnableHotReload = true);
    }
#endif

    private static string? GetTestExtensionDllPath()
    {
        var testDir = Path.GetDirectoryName(typeof(HotReloadTests).Assembly.Location)!;
        var configDir = Path.GetDirectoryName(testDir)!;
        var config = Path.GetFileName(configDir);
        var extensionDir = Path.Combine(testDir, "..", "..", "..", "..",
            "AdocNet.TestExtension", "bin", config!, "net10.0");
        var path = Path.GetFullPath(Path.Combine(extensionDir, "AdocNet.TestExtension.dll"));
        return File.Exists(path) ? path : null;
    }
}
