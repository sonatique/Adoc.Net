using BenchmarkDotNet.Attributes;
using AdocNet.Parser;

namespace AdocNet.Benchmarks;

[MemoryDiagnoser]
[ShortRunJob]
public class ParserBenchmarks
{
    private string _small = null!;
    private string _medium = null!;
    private string _large = null!;
    private string _tableHeavy = null!;
    private string _listHeavy = null!;

    [GlobalSetup]
    public void Setup()
    {
        _small = DocumentGenerator.Small();
        _medium = DocumentGenerator.Medium();
        _large = DocumentGenerator.Large();
        _tableHeavy = DocumentGenerator.TableHeavy();
        _listHeavy = DocumentGenerator.ListHeavy();
    }

    [Benchmark(Description = "Parse: Small (~1KB)")]
    public object ParseSmall() => AdocParser.Parse(_small);

    [Benchmark(Description = "Parse: Medium (~50KB)")]
    public object ParseMedium() => AdocParser.Parse(_medium);

    [Benchmark(Description = "Parse: Large (~500KB)")]
    public object ParseLarge() => AdocParser.Parse(_large);

    [Benchmark(Description = "Parse: Table-heavy")]
    public object ParseTableHeavy() => AdocParser.Parse(_tableHeavy);

    [Benchmark(Description = "Parse: List-heavy")]
    public object ParseListHeavy() => AdocParser.Parse(_listHeavy);
}
