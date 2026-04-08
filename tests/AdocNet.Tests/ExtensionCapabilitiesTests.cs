using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Extensions;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class ExtensionCapabilitiesTests
{
    private const string SimpleDoc = "= Title\n\nHello *world*.\n";

    // ── Test processors ────────────────────────────────────────────────

    /// <summary>Deterministic processor: no-op, declares deterministic.</summary>
    private sealed class DeterministicDocProcessor : IDocumentProcessor, IExtensionCapabilities
    {
        public bool IsDeterministic => true;

        public bool Process(DocumentNode document, RenderContext context)
        {
            // No-op — deterministic processor used for cache behavior testing
            return false;
        }
    }

    /// <summary>Non-deterministic processor: declares non-deterministic.</summary>
    private sealed class NonDeterministicDocProcessor : IDocumentProcessor, IExtensionCapabilities
    {
        public bool IsDeterministic => false;

        public bool Process(DocumentNode document, RenderContext context)
        {
            // No-op — non-deterministic declaration disables render cache
            return false;
        }
    }

    /// <summary>Processor that does NOT implement IExtensionCapabilities.</summary>
    private sealed class UndeclaredDocProcessor : IDocumentProcessor
    {
        public bool Process(DocumentNode document, RenderContext context)
        {
            // No-op — absence of IExtensionCapabilities = non-deterministic
            return false;
        }
    }

    /// <summary>Deterministic block processor.</summary>
    private sealed class DeterministicBlockProcessor : IBlockProcessor, IExtensionCapabilities
    {
        public bool IsDeterministic => true;
        public bool CanProcess(BlockNode node) => false;
        public bool Process(BlockNode node, RenderContext context) { return false; }
    }

    // ── Render cache with deterministic extensions ──────────────────────

    [Test]
    public void RenderCache_AllDeterministic_CacheHitOnSecondCall()
    {
        int parseCount = 0;
        var engine = new AdocEngine(new HtmlRenderer(), s =>
        {
            parseCount++;
            return AdocParser.Parse(s).Document;
        })
        {
            EnableCaching = true
        };
        engine.RegisterDocumentProcessor(new DeterministicDocProcessor());

        RenderToBytes(engine, SimpleDoc); // parse + render (count=1)
        RenderToBytes(engine, SimpleDoc); // render cache hit (count=1)

        Assert.That(parseCount, Is.EqualTo(1),
            "Deterministic extension: second call should use render cache, no re-parse");
    }

    [Test]
    public void RenderCache_NonDeterministic_NoRenderCacheHit()
    {
        int parseCount = 0;
        var engine = new AdocEngine(new HtmlRenderer(), s =>
        {
            parseCount++;
            return AdocParser.Parse(s).Document;
        })
        {
            EnableCaching = true
        };
        engine.RegisterDocumentProcessor(new NonDeterministicDocProcessor());

        RenderToBytes(engine, SimpleDoc); // parse + render (count=1)
        RenderToBytes(engine, SimpleDoc); // no render cache, must re-parse (count=2)

        Assert.That(parseCount, Is.EqualTo(2),
            "Non-deterministic extension: render cache disabled, must re-parse each time");
    }

    [Test]
    public void RenderCache_NoExtensions_CacheWorks()
    {
        int parseCount = 0;
        var engine = new AdocEngine(new HtmlRenderer(), s =>
        {
            parseCount++;
            return AdocParser.Parse(s).Document;
        })
        {
            EnableCaching = true
        };

        RenderToBytes(engine, SimpleDoc);
        RenderToBytes(engine, SimpleDoc);

        Assert.That(parseCount, Is.EqualTo(1),
            "No extensions: render cache should work as before");
    }

    [Test]
    public void RenderCache_UndeclaredCapabilities_TreatedAsNonDeterministic()
    {
        int parseCount = 0;
        var engine = new AdocEngine(new HtmlRenderer(), s =>
        {
            parseCount++;
            return AdocParser.Parse(s).Document;
        })
        {
            EnableCaching = true
        };
        engine.RegisterDocumentProcessor(new UndeclaredDocProcessor());

        RenderToBytes(engine, SimpleDoc);
        RenderToBytes(engine, SimpleDoc);

        Assert.That(parseCount, Is.EqualTo(2),
            "Processor without IExtensionCapabilities: treated as non-deterministic");
    }

    [Test]
    public void RenderCache_MixedDeterminism_CacheDisabled()
    {
        int parseCount = 0;
        var engine = new AdocEngine(new HtmlRenderer(), s =>
        {
            parseCount++;
            return AdocParser.Parse(s).Document;
        })
        {
            EnableCaching = true
        };
        engine.RegisterDocumentProcessor(new DeterministicDocProcessor());
        engine.RegisterDocumentProcessor(new NonDeterministicDocProcessor());

        RenderToBytes(engine, SimpleDoc);
        RenderToBytes(engine, SimpleDoc);

        Assert.That(parseCount, Is.EqualTo(2),
            "Mixed determinism: one non-deterministic disables render cache");
    }

    // ── Byte-identical correctness ─────────────────────────────────────

    [Test]
    public void DeterministicExtension_CachedOutput_ByteIdenticalToUncached()
    {
        // Render without caching
        var uncached = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document);
        uncached.RegisterDocumentProcessor(new DeterministicDocProcessor());
        var expected = RenderToBytes(uncached, SimpleDoc);

        // Render with caching — first call populates, second is cache hit
        var cached = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document)
        {
            EnableCaching = true
        };
        cached.RegisterDocumentProcessor(new DeterministicDocProcessor());
        RenderToBytes(cached, SimpleDoc); // populate
        var result = RenderToBytes(cached, SimpleDoc); // cache hit

        Assert.That(result, Is.EqualTo(expected),
            "Cached output with deterministic extensions must be byte-identical to uncached");
    }

    // ── Multiple processor types ───────────────────────────────────────

    [Test]
    public void RenderCache_AllTypesOfProcessorsDeterministic_CacheWorks()
    {
        int parseCount = 0;
        var engine = new AdocEngine(new HtmlRenderer(), s =>
        {
            parseCount++;
            return AdocParser.Parse(s).Document;
        })
        {
            EnableCaching = true
        };
        engine.RegisterDocumentProcessor(new DeterministicDocProcessor());
        engine.RegisterBlockProcessor(new DeterministicBlockProcessor());

        RenderToBytes(engine, SimpleDoc);
        RenderToBytes(engine, SimpleDoc);

        Assert.That(parseCount, Is.EqualTo(1),
            "All processor types deterministic: render cache should work");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static byte[] RenderToBytes(AdocEngine engine, string input)
    {
        using var ms = new MemoryStream();
        engine.Convert(input, ms);
        return ms.ToArray();
    }
}
