using BenchmarkDotNet.Attributes;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class RendererBenchmarks
{
    private DocumentNode _small = null!;
    private DocumentNode _medium = null!;
    private DocumentNode _large = null!;
    private DocumentNode _tableHeavy = null!;
    private DocumentNode _listHeavy = null!;

    [GlobalSetup]
    public void Setup()
    {
        _small = AdocParser.Parse(DocumentGenerator.Small()).Document;
        _medium = AdocParser.Parse(DocumentGenerator.Medium()).Document;
        _large = AdocParser.Parse(DocumentGenerator.Large()).Document;
        _tableHeavy = AdocParser.Parse(DocumentGenerator.TableHeavy()).Document;
        _listHeavy = AdocParser.Parse(DocumentGenerator.ListHeavy()).Document;
    }

    [Benchmark(Description = "Render: Small (~1KB)")]
    public string RenderSmall() => new HtmlRenderer().RenderToString(_small);

    [Benchmark(Description = "Render: Medium (~50KB)")]
    public string RenderMedium() => new HtmlRenderer().RenderToString(_medium);

    [Benchmark(Description = "Render: Large (~500KB)")]
    public string RenderLarge() => new HtmlRenderer().RenderToString(_large);

    [Benchmark(Description = "Render: Table-heavy")]
    public string RenderTableHeavy() => new HtmlRenderer().RenderToString(_tableHeavy);

    [Benchmark(Description = "Render: List-heavy")]
    public string RenderListHeavy() => new HtmlRenderer().RenderToString(_listHeavy);
}
