using AdocNet.Converters.Man;
using AdocNet.Parser;
using AdocNet.Tools.DifferentialTester;

namespace AdocNet.Tests;

/// <summary>
/// Compares AdocNet man page output against Asciidoctor reference output.
/// Uses manpage-doctype fixture files from spec/manpage/.
/// </summary>
[TestFixture]
[Category("Compatibility")]
[Category("ManPage")]
public class ManCompatibilityTests
{
    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ManPageDir = Path.Combine(RepoRoot, "spec", "manpage");

    private static bool _asciidoctorAvailable;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _asciidoctorAvailable = AsciidoctorRunner.IsAvailable();
        if (!_asciidoctorAvailable)
            Assert.Ignore("Asciidoctor is not installed.");
    }

    /// <summary>
    /// Discovers all .adoc manpage fixture files.
    /// </summary>
    public static IEnumerable<TestCaseData> ManPageFiles()
    {
        var repoRoot = FindRepoRoot();
        var manPageDir = Path.Combine(repoRoot, "spec", "manpage");

        if (!Directory.Exists(manPageDir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(manPageDir, "*.adoc", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            yield return new TestCaseData(file).SetName($"ManPage: {name}");
        }
    }

    [Test]
    [TestCaseSource(nameof(ManPageFiles))]
    public void ManPage_matches_Asciidoctor(string adocFilePath)
    {
        if (!_asciidoctorAvailable)
        {
            Assert.Inconclusive("Asciidoctor not available");
            return;
        }

        // Render with AdocNet
        var sourceText = File.ReadAllText(adocFilePath);
        var parseOptions = new ParseOptions
        {
            SourceFilePath = adocFilePath,
            Attributes = new Dictionary<string, string> { ["doctype"] = "manpage" },
        };
        var parseResult = AdocParser.Parse(sourceText, parseOptions);
        var renderer = new ManRenderer();
        var adocNetMan = renderer.RenderToString(parseResult.Document);

        // Render with Asciidoctor
        var asciidoctorResult = AsciidoctorRunner.RenderManPage(adocFilePath);

        if (asciidoctorResult?.Html is null)
        {
            var reason = asciidoctorResult?.TimedOut == true ? "Asciidoctor timed out" : "Asciidoctor failed";
            Assert.Inconclusive($"{reason}: {asciidoctorResult?.Stderr}");
            return;
        }

        // Normalize and compare
        var normalizedAdocNet = ManNormalizer.Normalize(adocNetMan);
        var normalizedAsciidoctor = ManNormalizer.Normalize(asciidoctorResult.Html);

        var diff = DiffEngine.Compare(normalizedAsciidoctor, normalizedAdocNet);

        if (!diff.Identical)
        {
            var diffSummary = string.Join("\n", diff.Lines.Take(60).Select(l =>
                l.Op switch
                {
                    DiffOp.Add => $"+ {l.Content}",
                    DiffOp.Remove => $"- {l.Content}",
                    DiffOp.Separator => "  ...",
                    _ => $"  {l.Content}",
                }));

            Assert.Warn(
                $"Man page output differs from Asciidoctor (similarity: {diff.Similarity:P1})\n" +
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
