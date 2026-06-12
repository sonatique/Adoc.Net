using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Extensions;
using AdocNet.Parser;

namespace AdocNet.Tests;

[TestFixture]
public class CachingTests
{
    private const string SimpleDoc = "= Title\n\nHello *world*.\n";
    private const string AltDoc = "= Other\n\nDifferent content.\n";

    private static AdocEngine CreateEngine(bool caching = true)
    {
        var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document)
        {
            EnableCaching = caching
        };
        return engine;
    }

    // ── EnableCaching defaults ──────────────────────────────────────────

    [Test]
    public void EnableCaching_DefaultsFalse()
    {
        var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document);
        Assert.That(engine.EnableCaching, Is.False);
    }

    [Test]
    public void MaxCacheEntries_DefaultsSixteen()
    {
        var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document);
        Assert.That(engine.MaxCacheEntries, Is.EqualTo(16));
    }

    [Test]
    public void CachingDisabled_ProducesCorrectOutput()
    {
        var engine = CreateEngine(caching: false);
        var result1 = RenderToBytes(engine, SimpleDoc);
        var result2 = RenderToBytes(engine, SimpleDoc);
        Assert.That(result1, Is.EqualTo(result2));
    }

    // ── Parse cache ─────────────────────────────────────────────────────

    [Test]
    public void ParseCacheHit_SameInput_ReusesAst()
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

        Assert.That(parseCount, Is.EqualTo(1), "Parser should be called only once for same input");
    }

    [Test]
    public void ParseCacheMiss_DifferentInput_ReparsesBoth()
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
        RenderToBytes(engine, AltDoc);

        Assert.That(parseCount, Is.EqualTo(2), "Parser should be called for each unique input");
    }

    // ── Render cache ────────────────────────────────────────────────────

    [Test]
    public void RenderCacheHit_SameInputAndOptions_ReturnsCachedBytes()
    {
        var engine = CreateEngine();
        var first = RenderToBytes(engine, SimpleDoc);
        var second = RenderToBytes(engine, SimpleDoc);
        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void RenderCacheMiss_DifferentOptions_ProducesDifferentOutput()
    {
        var engine = CreateEngine();

        var fragment = RenderToBytes(engine, SimpleDoc, new HtmlRenderOptions());
        var fullDoc = RenderToBytes(engine, SimpleDoc, new HtmlRenderOptions { FullDocument = true });

        Assert.That(fragment, Is.Not.EqualTo(fullDoc),
            "Different options should produce different output");
    }

    // ── Correctness: byte-identical ─────────────────────────────────────

    [Test]
    public void CachedOutput_ByteIdentical_ToUncachedOutput()
    {
        // Render without caching
        var uncached = CreateEngine(caching: false);
        var expected = RenderToBytes(uncached, SimpleDoc);

        // Render with caching (first call = miss, populates cache)
        var cached = CreateEngine();
        var firstCached = RenderToBytes(cached, SimpleDoc);

        // Render with caching (second call = cache hit)
        var secondCached = RenderToBytes(cached, SimpleDoc);

        Assert.That(firstCached, Is.EqualTo(expected), "First cached render must match uncached");
        Assert.That(secondCached, Is.EqualTo(expected), "Cache hit must match uncached");
    }

    [Test]
    public void CachedOutput_ByteIdentical_MediumDocument()
    {
        var input = GenerateMediumDoc();

        var uncached = CreateEngine(caching: false);
        var expected = RenderToBytes(uncached, input);

        var cached = CreateEngine();
        RenderToBytes(cached, input); // populate
        var result = RenderToBytes(cached, input); // cache hit

        Assert.That(result, Is.EqualTo(expected));
    }

    // ── LRU eviction ────────────────────────────────────────────────────

    [Test]
    public void LruEviction_OldestEvicted_WhenCacheFull()
    {
        int parseCount = 0;
        var engine = new AdocEngine(new HtmlRenderer(), s =>
        {
            parseCount++;
            return AdocParser.Parse(s).Document;
        })
        {
            EnableCaching = true,
            MaxCacheEntries = 2
        };

        var doc1 = "= Doc 1\n\nContent one.\n";
        var doc2 = "= Doc 2\n\nContent two.\n";
        var doc3 = "= Doc 3\n\nContent three.\n";

        RenderToBytes(engine, doc1); // parse 1 (count=1)
        RenderToBytes(engine, doc2); // parse 2 (count=2), cache full
        RenderToBytes(engine, doc3); // parse 3 (count=3), evicts doc1

        parseCount = 0;
        RenderToBytes(engine, doc1); // doc1 evicted, must re-parse (count=1)
        Assert.That(parseCount, Is.EqualTo(1), "Evicted entry should require re-parse");

        parseCount = 0;
        RenderToBytes(engine, doc3); // doc3 still cached (count=0)
        Assert.That(parseCount, Is.EqualTo(0), "Recent entry should be cached");
    }

    // ── ClearCache ──────────────────────────────────────────────────────

    [Test]
    public void ClearCache_ForcesReparse()
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

        RenderToBytes(engine, SimpleDoc); // parse (count=1)
        RenderToBytes(engine, SimpleDoc); // cache hit (count=1)
        engine.ClearCache();
        RenderToBytes(engine, SimpleDoc); // re-parse (count=2)

        Assert.That(parseCount, Is.EqualTo(2));
    }

    [Test]
    public void DisablingCaching_ClearsCache()
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

        RenderToBytes(engine, SimpleDoc); // parse (count=1)
        engine.EnableCaching = false;
        engine.EnableCaching = true;
        RenderToBytes(engine, SimpleDoc); // cache cleared, re-parse (count=2)

        Assert.That(parseCount, Is.EqualTo(2));
    }

    // ── Extensions + caching ────────────────────────────────────────────

    [Test]
    public void CachingWithExtensions_ProducesCorrectOutput()
    {
        var uncachedEngine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document);
        uncachedEngine.RegisterDocumentProcessor(new DocumentMetadataProcessor("test-meta"));
        var expected = RenderToBytes(uncachedEngine, SimpleDoc);

        var cachedEngine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document)
        {
            EnableCaching = true
        };
        cachedEngine.RegisterDocumentProcessor(new DocumentMetadataProcessor("test-meta"));
        RenderToBytes(cachedEngine, SimpleDoc); // populate
        var result = RenderToBytes(cachedEngine, SimpleDoc); // cache hit

        Assert.That(result, Is.EqualTo(expected));
    }

    // ── Thread safety (basic) ───────────────────────────────────────────

    [Test]
    public void ConcurrentConvert_DoesNotThrow()
    {
        var engine = CreateEngine();
        var inputs = Enumerable.Range(0, 20)
            .Select(i => $"= Doc {i}\n\nContent {i}.\n")
            .ToArray();

        Assert.DoesNotThrow(() =>
        {
            Parallel.ForEach(inputs, input =>
            {
                RenderToBytes(engine, input);
            });
        });
    }

    [Test]
    public void ConcurrentConvert_WithExtensions_DoesNotThrowOrCorrupt()
    {
        // Regression: with a processor registered, concurrent Convert calls raced the freeze/
        // failure-tracking state and shared a parse-cached AST with the mutating pipeline.
        var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document)
        {
            EnableCaching = true
        };
        engine.RegisterDocumentProcessor(new DocumentMetadataProcessor("test-meta"));

        var inputs = Enumerable.Range(0, 50)
            .Select(i => $"= Doc {i}\n\nContent {i}.\n")
            .ToArray();

        Assert.DoesNotThrow(() =>
        {
            Parallel.For(0, 200, k =>
            {
                var input = inputs[k % inputs.Length];
                RenderToBytes(engine, input);
            });
        });
    }

    private sealed class FixedParagraphTemplate : INodeTemplate
    {
        private readonly string _marker;
        public FixedParagraphTemplate(string marker) => _marker = marker;
        public bool CanRender(AstNode node) => node is ParagraphNode;
        public string Render(AstNode node, RenderContext context) => $"<p data-tmpl=\"{_marker}\">x</p>\n";
    }

    [Test]
    public void RenderCache_distinguishes_options_with_different_templates()
    {
        // Regression: the render-cache key hashed collection-valued options via ToString(), which
        // for a template list is just the type name — so a second render with different templates
        // returned the first render's cached (stale) output.
        var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document)
        {
            EnableCaching = true
        };

        var optsA = new HtmlRenderOptions { Templates = new INodeTemplate[] { new FixedParagraphTemplate("A") } };
        var optsB = new HtmlRenderOptions { Templates = new INodeTemplate[] { new FixedParagraphTemplate("B") } };

        var a = System.Text.Encoding.UTF8.GetString(RenderToBytes(engine, SimpleDoc, optsA));
        var b = System.Text.Encoding.UTF8.GetString(RenderToBytes(engine, SimpleDoc, optsB));

        Assert.That(a, Does.Contain("data-tmpl=\"A\""));
        Assert.That(b, Does.Contain("data-tmpl=\"B\""), "second render must not serve the first render's cached output");
    }

    [Test]
    public void Engine_is_disposable_and_dispose_is_idempotent()
    {
        var engine = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document);
        Assert.That(engine, Is.InstanceOf<IDisposable>());
        Assert.DoesNotThrow(() =>
        {
            engine.Dispose();
            engine.Dispose(); // idempotent
        });

        // The using pattern works.
        Assert.DoesNotThrow(() =>
        {
            using var scoped = new AdocEngine(new HtmlRenderer(), s => AdocParser.Parse(s).Document);
            RenderToBytes(scoped, SimpleDoc);
        });
    }

    // ── MaxCacheEntries validation ──────────────────────────────────────

    [Test]
    public void MaxCacheEntries_ThrowsOnZero()
    {
        var engine = CreateEngine();
        Assert.Throws<ArgumentOutOfRangeException>(() => engine.MaxCacheEntries = 0);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static byte[] RenderToBytes(AdocEngine engine, string input, RenderOptions? options = null)
    {
        using var ms = new MemoryStream();
        engine.Convert(input, ms, options);
        return ms.ToArray();
    }

    private static string GenerateMediumDoc()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("= Medium Test Document");
        sb.AppendLine();
        for (int i = 1; i <= 10; i++)
        {
            sb.AppendLine($"== Section {i}");
            sb.AppendLine();
            sb.AppendLine($"Paragraph with *bold* and _italic_ text in section {i}.");
            sb.AppendLine();
            for (int j = 0; j < 5; j++)
                sb.AppendLine($"* Item {j + 1} in section {i}");
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
