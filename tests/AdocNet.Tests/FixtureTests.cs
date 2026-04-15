using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Discovers .adoc fixture files under spec/fixtures/ and validates the parser + renderer
/// produce the expected AST dump (.ast.txt) and HTML output (.html) for each fixture.
/// </summary>
[TestFixture]
public class FixtureTests
{
    private static string? _repoRoot;

    private static string RepoRoot => _repoRoot ??= FindRepoRoot();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AdocNet.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            "Could not find repo root (AdocNet.slnx). " +
            "Ensure tests are run from within the repository.");
    }

    private static IEnumerable<TestCaseData> DiscoverFixtures()
    {
        var fixturesDir = Path.Combine(RepoRoot, "spec", "fixtures");
        if (!Directory.Exists(fixturesDir))
            yield break;

        var adocFiles = Directory.GetFiles(fixturesDir, "*.adoc", SearchOption.AllDirectories);
        Array.Sort(adocFiles, StringComparer.Ordinal);

        foreach (var adocPath in adocFiles)
        {
            // Skip include fragments (e.g., _indent-target.adoc)
            if (Path.GetFileName(adocPath).StartsWith('_'))
                continue;

            var relativePath = Path.GetRelativePath(fixturesDir, adocPath)
                .Replace('\\', '/');
            var testName = Path.ChangeExtension(relativePath, null);
            yield return new TestCaseData(adocPath).SetName(testName);
        }
    }

    [TestCaseSource(nameof(DiscoverFixtures))]
    public void Fixture_ast_matches_expected(string adocPath)
    {
        var expectedPath = Path.ChangeExtension(adocPath, ".ast.txt");
        if (!File.Exists(expectedPath))
            Assert.Ignore($"No .ast.txt file for fixture: {Path.GetFileNameWithoutExtension(adocPath)}");

        var input = ExpandIncludes(adocPath);
        var result = BlockParser.Parse(input);
        var actual = AstPrettyPrinter.Print(result.Document, includeSourceRanges: false);
        var expected = NormalizeLineEndings(File.ReadAllText(expectedPath));

        Assert.That(actual, Is.EqualTo(expected),
            $"AST mismatch for {Path.GetFileNameWithoutExtension(adocPath)}");
    }

    [TestCaseSource(nameof(DiscoverFixtures))]
    public void Fixture_html_matches_expected(string adocPath)
    {
        var expectedPath = Path.ChangeExtension(adocPath, ".html");
        if (!File.Exists(expectedPath))
            Assert.Ignore($"No .html file for fixture: {Path.GetFileNameWithoutExtension(adocPath)}");

        var input = ExpandIncludes(adocPath);
        var result = BlockParser.Parse(input);
        var actual = new HtmlRenderer().RenderToString(result.Document);
        var expected = NormalizeLineEndings(File.ReadAllText(expectedPath));

        Assert.That(actual, Is.EqualTo(expected),
            $"HTML mismatch for {Path.GetFileNameWithoutExtension(adocPath)}");
    }

    /// <summary>
    /// Reads a fixture file, expands any include:: directives, and runs
    /// the conditional preprocessor (ifdef/ifndef/ifeval).
    /// For fixtures without includes or conditionals this is effectively a
    /// no-op (identity transform).
    /// </summary>
    private static string ExpandIncludes(string adocPath)
    {
        var text = File.ReadAllText(adocPath);
        var baseDir = Path.GetDirectoryName(adocPath)!;
        var expandResult = IncludeExpander.Expand(text, baseDir);
        // Pass default attributes so conditionals like ifdef::backend work correctly.
        var defaults = BlockParser.GetDefaultAttributes();
        var (filteredText, _) = ConditionalPreprocessor.Process(expandResult.Text, defaults);
        return filteredText;
    }

    /// <summary>
    /// Normalizes line endings in expected files to \n so fixtures work on all platforms.
    /// </summary>
    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n").Replace("\r", "\n");
}
