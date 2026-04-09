using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Guards against accidental allocation regressions in the parser and renderer.
/// These tests measure GC collections during a known workload and fail if
/// allocations exceed the established baseline by more than 20%.
///
/// Rule: if a change causes these tests to fail, either the allocation increase
/// is justified (update the baseline) or the change introduced a regression.
/// </summary>
[TestFixture]
public class AllocationGuardTests
{
    // A medium-complexity document (~50KB) that exercises most parser paths.
    private string _mediumDoc = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("= Allocation Test Document");
        sb.AppendLine(":author: Test");
        sb.AppendLine();

        for (int i = 1; i <= 10; i++)
        {
            sb.AppendLine($"== Section {i}");
            sb.AppendLine();
            for (int p = 0; p < 5; p++)
            {
                sb.AppendLine($"Paragraph {p} with *bold*, _italic_, and `mono` in section {i}. " +
                    $"Link: https://example.com/{i}/{p}");
                sb.AppendLine();
            }
            for (int item = 0; item < 5; item++)
                sb.AppendLine($"* Item {item} with *formatting*");
            sb.AppendLine();
            sb.AppendLine("[source,csharp]");
            sb.AppendLine("----");
            sb.AppendLine($"Console.WriteLine(\"{i}\");");
            sb.AppendLine("----");
            sb.AppendLine();
        }

        _mediumDoc = sb.ToString();

        // Warm up JIT
        var warmup = AdocParser.Parse(_mediumDoc);
        new HtmlRenderer().RenderToString(warmup.Document);
    }

    [Test]
    public void Parse_MediumDocument_AllocationWithinBaseline()
    {
        // Baseline: parsing a ~50KB document should allocate < 850 KB
        // This catches regressions like accidental string copies or list resizing
        // Bumped from 800KB after beta.15 added StructuralHash fields to AstNode
        const long maxBytes = 850 * 1024;

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var result = AdocParser.Parse(_mediumDoc);
        var after = GC.GetTotalAllocatedBytes(precise: true);

        var allocated = after - before;
        Assert.That(allocated, Is.LessThan(maxBytes),
            $"Parser allocated {allocated / 1024}KB, baseline is {maxBytes / 1024}KB. " +
            "If this increase is intentional, update the baseline.");
    }

    [Test]
    public void Render_MediumDocument_AllocationWithinBaseline()
    {
        // Baseline: rendering a ~50KB document to HTML should allocate < 400 KB
        const long maxBytes = 400 * 1024;

        var doc = AdocParser.Parse(_mediumDoc).Document;

        var before = GC.GetTotalAllocatedBytes(precise: true);
        var html = new HtmlRenderer().RenderToString(doc);
        var after = GC.GetTotalAllocatedBytes(precise: true);

        var allocated = after - before;
        Assert.That(allocated, Is.LessThan(maxBytes),
            $"Renderer allocated {allocated / 1024}KB, baseline is {maxBytes / 1024}KB. " +
            "If this increase is intentional, update the baseline.");
    }
}
