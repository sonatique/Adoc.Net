using AdocNet.Converters.DocBook;
using AdocNet.Parser;
using AdocNet.Tools.DifferentialTester;

namespace AdocNet.Tests;

/// <summary>
/// Compares AdocNet DocBook XML output against Asciidoctor reference output.
/// Uses the same fixture files as the HTML compatibility tests but renders to DocBook.
/// </summary>
[TestFixture]
[Category("Compatibility")]
[Category("DocBook")]
public class DocBookCompatibilityTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string FixturesDir = Path.Combine(RepoRoot, "spec", "fixtures");

    private static bool _asciidoctorAvailable;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _asciidoctorAvailable = AsciidoctorRunner.IsAvailable();
        if (!_asciidoctorAvailable)
            Assert.Ignore("Asciidoctor is not installed.");
    }

    /// <summary>
    /// Discovers all .adoc fixture files that are suitable for DocBook testing.
    /// Skips files that use features not applicable to DocBook output.
    /// </summary>
    public static IEnumerable<TestCaseData> DocBookFixtureFiles()
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

            // Skip fixture categories that don't translate well to DocBook
            var relativePath = Path.GetRelativePath(fixturesDir, file);
            var category = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];

            // Skip rendering-specific fixtures (HTML-only features)
            if (category is "rendering" or "diagnostics" or "comments" or "substitutions")
                continue;

            yield return new TestCaseData(file).SetName($"DocBook: {relativePath}");
        }
    }

    [Test]
    [TestCaseSource(nameof(DocBookFixtureFiles))]
    public void DocBook_matches_Asciidoctor(string adocFilePath)
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
        var renderer = new DocBookRenderer();
        var adocNetXml = renderer.RenderToString(parseResult.Document);

        // Render with Asciidoctor
        var asciidoctorResult = AsciidoctorRunner.RenderDocBook(adocFilePath);

        if (asciidoctorResult?.Html is null)
        {
            var reason = asciidoctorResult?.TimedOut == true ? "Asciidoctor timed out" : "Asciidoctor failed";
            Assert.Inconclusive($"{reason}: {asciidoctorResult?.Stderr}");
            return;
        }

        // Normalize and compare
        var normalizedAdocNet = XmlNormalizer.Normalize(adocNetXml, XmlSource.AdocNet);
        var normalizedAsciidoctor = XmlNormalizer.Normalize(asciidoctorResult.Html, XmlSource.Asciidoctor);

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
                $"DocBook output differs from Asciidoctor (similarity: {diff.Similarity:P1})\n" +
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
        return Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", ".."));
    }
}
