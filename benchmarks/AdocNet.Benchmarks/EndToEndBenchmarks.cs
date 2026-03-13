using BenchmarkDotNet.Attributes;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class EndToEndBenchmarks
{
    private string _small = null!;
    private string _medium = null!;
    private string _large = null!;

    [GlobalSetup]
    public void Setup()
    {
        _small = DocumentGenerator.Small();
        _medium = DocumentGenerator.Medium();
        _large = DocumentGenerator.Large();
    }

    [Benchmark(Description = "E2E: Small (~1KB)")]
    public string EndToEndSmall()
    {
        var result = AdocParser.Parse(_small);
        return new HtmlRenderer().RenderToString(result.Document);
    }

    [Benchmark(Description = "E2E: Medium (~50KB)")]
    public string EndToEndMedium()
    {
        var result = AdocParser.Parse(_medium);
        return new HtmlRenderer().RenderToString(result.Document);
    }

    [Benchmark(Description = "E2E: Large (~500KB)")]
    public string EndToEndLarge()
    {
        var result = AdocParser.Parse(_large);
        return new HtmlRenderer().RenderToString(result.Document);
    }
}
