using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class PersistentCacheTests
{
    private const string SimpleDoc = "= Title\n\nHello *world*.\n";
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "adocnet-test-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ── Persist + retrieve ─────────────────────────────────────────────

    [Test]
    public void PersistentCache_PersistAndRetrieveAcrossEngines()
    {
        // Engine 1: render and persist to disk
        var engine1 = CreateEngine(persistent: true);
        var first = RenderToBytes(engine1, SimpleDoc);

        // Engine 2: new instance, same cache dir — should find disk cache
        int parseCount = 0;
        var engine2 = new AdocEngine(new HtmlRenderer(), s =>
        {
            parseCount++;
            return AdocParser.Parse(s).Document;
        })
        {
            EnableCaching = true,
            EnablePersistentCache = true,
            PersistentCacheDirectory = _tempDir
        };

        var second = RenderToBytes(engine2, SimpleDoc);

        Assert.That(second, Is.EqualTo(first), "Disk-cached output must match original");
        Assert.That(parseCount, Is.EqualTo(0),
            "Second engine should use persistent cache without parsing");
    }

    // ── Version mismatch ───────────────────────────────────────────────

    [Test]
    public void PersistentCache_VersionMismatch_EntryIgnored()
    {
        // Write a cache entry with a fake version by using the store directly
        var store = CreateStore("0.0.0-fake");
        store.Save("test-key", new byte[] { 1, 2, 3 });

        // Try to load with a different version
        var currentStore = CreateStore(); // uses real engine version
        var found = currentStore.TryLoad("test-key", out var value);

        Assert.That(found, Is.False, "Version mismatch should cause cache miss");
    }

    // ── ClearCache clears disk ─────────────────────────────────────────

    [Test]
    public void ClearCache_RemovesDiskFiles()
    {
        var engine = CreateEngine(persistent: true);
        RenderToBytes(engine, SimpleDoc); // persist to disk

        var cacheSubDir = Path.Combine(_tempDir, "v1");
        Assert.That(Directory.Exists(cacheSubDir) && Directory.GetFiles(cacheSubDir, "*.bin").Length > 0,
            Is.True, "Cache files should exist after render");

        engine.ClearCache();

        var remainingFiles = Directory.Exists(cacheSubDir)
            ? Directory.GetFiles(cacheSubDir, "*.bin").Length
            : 0;
        Assert.That(remainingFiles, Is.EqualTo(0), "ClearCache should remove disk files");
    }

    // ── Disabled by default ────────────────────────────────────────────

    [Test]
    public void PersistentCache_DisabledByDefault_NoDiskWrites()
    {
        var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document)
        {
            EnableCaching = true,
            PersistentCacheDirectory = _tempDir
            // EnablePersistentCache NOT set (default false)
        };

        RenderToBytes(engine, SimpleDoc);

        var cacheSubDir = Path.Combine(_tempDir, "v1");
        var hasFiles = Directory.Exists(cacheSubDir) && Directory.GetFiles(cacheSubDir).Length > 0;
        Assert.That(hasFiles, Is.False, "Persistent cache disabled: no files should be written");
    }

    // ── Byte-identical correctness ─────────────────────────────────────

    [Test]
    public void PersistentCache_ByteIdenticalToFreshRender()
    {
        // Fresh render without any caching
        var uncachedEngine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document);
        var expected = RenderToBytes(uncachedEngine, SimpleDoc);

        // Render with persistent cache — populate
        var engine1 = CreateEngine(persistent: true);
        RenderToBytes(engine1, SimpleDoc);

        // New engine — retrieve from disk
        var engine2 = CreateEngine(persistent: true);
        var fromDisk = RenderToBytes(engine2, SimpleDoc);

        Assert.That(fromDisk, Is.EqualTo(expected),
            "Persistent cache output must be byte-identical to uncached render");
    }

    // ── Max entries eviction ───────────────────────────────────────────

    [Test]
    public void PersistentCache_EvictsOldestWhenFull()
    {
        var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document)
        {
            EnableCaching = true,
            EnablePersistentCache = true,
            PersistentCacheDirectory = _tempDir,
            MaxPersistentCacheEntries = 2
        };

        // Render 3 documents — should evict the oldest
        for (int i = 0; i < 3; i++)
        {
            var doc = $"= Doc {i}\n\nContent {i}.\n";
            RenderToBytes(engine, doc);
        }

        var cacheSubDir = Path.Combine(_tempDir, "v1");
        var fileCount = Directory.Exists(cacheSubDir)
            ? Directory.GetFiles(cacheSubDir, "*.bin").Length
            : 0;

        Assert.That(fileCount, Is.LessThanOrEqualTo(2),
            "Should evict oldest entries to stay within max");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private AdocEngine CreateEngine(bool persistent = false)
    {
        return new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document)
        {
            EnableCaching = true,
            EnablePersistentCache = persistent,
            PersistentCacheDirectory = _tempDir
        };
    }

    private static byte[] RenderToBytes(AdocEngine engine, string input)
    {
        using var ms = new MemoryStream();
        engine.Convert(input, ms);
        return ms.ToArray();
    }

    private AdocNet.Caching.PersistentCacheStore CreateStore(string? version = null)
    {
        var v = version ?? AdocNet.Extensions.ExtensionDirectoryLoader.GetCurrentAdocNetVersion();
        return new AdocNet.Caching.PersistentCacheStore(_tempDir, v, 256);
    }
}
