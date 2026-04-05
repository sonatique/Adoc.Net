using BenchmarkDotNet.Attributes;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Benchmarks;

/// <summary>
/// Compares cold (uncached) vs warm (cache hit) Convert() performance.
/// Measures the speedup from parse + render caching.
/// </summary>
[MemoryDiagnoser]
[ShortRunJob]
public class CachedRenderBenchmarks
{
    private string _small = null!;
    private string _medium = null!;
    private string _large = null!;

    private AdocEngine _uncachedEngine = null!;
    private AdocEngine _cachedEngineSmall = null!;
    private AdocEngine _cachedEngineMedium = null!;
    private AdocEngine _cachedEngineLarge = null!;

    [GlobalSetup]
    public void Setup()
    {
        _small = DocumentGenerator.Small();
        _medium = DocumentGenerator.Medium();
        _large = DocumentGenerator.Large();

        var renderer = new HtmlRenderer();

        _uncachedEngine = new AdocEngine(renderer, s => AdocParser.Parse(s).Document);

        // Pre-warm cached engines so benchmarks measure cache hits
        _cachedEngineSmall = CreateCachedEngine(renderer);
        Warm(_cachedEngineSmall, _small);

        _cachedEngineMedium = CreateCachedEngine(renderer);
        Warm(_cachedEngineMedium, _medium);

        _cachedEngineLarge = CreateCachedEngine(renderer);
        Warm(_cachedEngineLarge, _large);
    }

    // ── Cold (uncached) baselines ───────────────────────────────────────

    [Benchmark(Description = "Cold: Small (~1KB)")]
    public void ColdSmall()
    {
        using var ms = new MemoryStream();
        _uncachedEngine.Convert(_small, ms);
    }

    [Benchmark(Description = "Cold: Medium (~50KB)")]
    public void ColdMedium()
    {
        using var ms = new MemoryStream();
        _uncachedEngine.Convert(_medium, ms);
    }

    [Benchmark(Description = "Cold: Large (~500KB)")]
    public void ColdLarge()
    {
        using var ms = new MemoryStream();
        _uncachedEngine.Convert(_large, ms);
    }

    // ── Warm (cache hit) ────────────────────────────────────────────────

    [Benchmark(Description = "Cached: Small (~1KB)")]
    public void CachedSmall()
    {
        using var ms = new MemoryStream();
        _cachedEngineSmall.Convert(_small, ms);
    }

    [Benchmark(Description = "Cached: Medium (~50KB)")]
    public void CachedMedium()
    {
        using var ms = new MemoryStream();
        _cachedEngineMedium.Convert(_medium, ms);
    }

    [Benchmark(Description = "Cached: Large (~500KB)")]
    public void CachedLarge()
    {
        using var ms = new MemoryStream();
        _cachedEngineLarge.Convert(_large, ms);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static AdocEngine CreateCachedEngine(HtmlRenderer renderer)
    {
        return new AdocEngine(renderer, s => AdocParser.Parse(s).Document)
        {
            EnableCaching = true
        };
    }

    private static void Warm(AdocEngine engine, string input)
    {
        using var ms = new MemoryStream();
        engine.Convert(input, ms);
    }
}
