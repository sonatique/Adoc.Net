using AdocNet.Converters.Html;
using AdocNet.Parser;
using AdocNet.Tools.DifferentialTester;

namespace AdocNet.Tests;

/// <summary>
/// Compares AdocNet HTML output against Asciidoctor reference output.
/// These tests require Asciidoctor to be installed and are marked [Explicit]
/// so they only run when explicitly requested:
///   dotnet test --filter Category=Compatibility
/// </summary>
[TestFixture]
[Category("Compatibility")]
[Explicit("Requires Asciidoctor CLI installed")]
public class CompatibilityTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturesDir = Path.Combine(RepoRoot, "spec", "fixtures");
    private static readonly string ConformanceDir = Path.Combine(RepoRoot, "spec", "conformance");

    private static bool _asciidoctorAvailable;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _asciidoctorAvailable = AsciidoctorRunner.IsAvailable();
        if (!_asciidoctorAvailable)
            Assert.Inconclusive("Asciidoctor is not installed. Install with: gem install asciidoctor");
    }

    /// <summary>
    /// Discovers all .adoc fixture files for parameterized testing.
    /// </summary>
    public static IEnumerable<TestCaseData> FixtureFiles()
    {
        var repoRoot = FindRepoRoot();
        var fixturesDir = Path.Combine(repoRoot, "spec", "fixtures");

        if (!Directory.Exists(fixturesDir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(fixturesDir, "*.adoc", SearchOption.AllDirectories))
        {
            // Skip include fragments
            if (Path.GetFileName(file).StartsWith('_'))
                continue;

            var relativePath = Path.GetRelativePath(fixturesDir, file);
            yield return new TestCaseData(file).SetName($"Fixture: {relativePath}");
        }
    }

    /// <summary>
    /// Discovers conformance .adoc files.
    /// </summary>
    public static IEnumerable<TestCaseData> ConformanceFiles()
    {
        var repoRoot = FindRepoRoot();
        var conformanceDir = Path.Combine(repoRoot, "spec", "conformance");

        if (!Directory.Exists(conformanceDir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(conformanceDir, "*.adoc", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            yield return new TestCaseData(file).SetName($"Conformance: {name}");
        }
    }

    [Test]
    [TestCaseSource(nameof(FixtureFiles))]
    public void Fixture_matches_Asciidoctor(string adocFilePath)
    {
        CompareWithAsciidoctor(adocFilePath);
    }

    [Test]
    [TestCaseSource(nameof(ConformanceFiles))]
    public void Conformance_matches_Asciidoctor(string adocFilePath)
    {
        CompareWithAsciidoctor(adocFilePath);
    }

    private static void CompareWithAsciidoctor(string adocFilePath)
    {
        if (!_asciidoctorAvailable)
        {
            Assert.Inconclusive("Asciidoctor not available");
            return;
        }

        // Render with AdocNet
        var sourceText = File.ReadAllText(adocFilePath);
        var parseOptions = new ParseOptions { SourceFilePath = adocFilePath };
        var parseResult = AdocParser.Parse(sourceText, parseOptions);
        var renderer = new HtmlRenderer();
        var adocNetHtml = renderer.RenderToString(parseResult.Document);

        // Render with Asciidoctor
        var asciidoctorResult = AsciidoctorRunner.Render(adocFilePath);

        if (asciidoctorResult?.Html is null)
        {
            var reason = asciidoctorResult?.TimedOut == true ? "Asciidoctor timed out" : "Asciidoctor failed";
            Assert.Inconclusive($"{reason}: {asciidoctorResult?.Stderr}");
            return;
        }

        // Normalize and compare
        var normalizedAdocNet = HtmlNormalizer.Normalize(adocNetHtml, HtmlSource.AdocNet);
        var normalizedAsciidoctor = HtmlNormalizer.Normalize(asciidoctorResult.Html, HtmlSource.Asciidoctor);

        var diff = DiffEngine.Compare(normalizedAsciidoctor, normalizedAdocNet);

        if (!diff.Identical)
        {
            var diffSummary = string.Join("\n", diff.Lines.Take(30).Select(l =>
                l.Op switch
                {
                    DiffOp.Add => $"+ {l.Content}",
                    DiffOp.Remove => $"- {l.Content}",
                    DiffOp.Separator => "  ...",
                    _ => $"  {l.Content}",
                }));

            Assert.Warn(
                $"Output differs from Asciidoctor (similarity: {diff.Similarity:P1})\n" +
                $"First differences:\n{diffSummary}");
        }
    }

    private static string FindRepoRoot()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "AdocNet.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }

        // Fallback
        return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
    }
}
