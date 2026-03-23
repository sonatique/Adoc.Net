using System.Diagnostics;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Integration tests comparing AdocNet output against Asciidoctor golden files,
/// and validating cross-TFM consistency (net10.0 vs netstandard2.0).
/// </summary>
[TestFixture]
public class IntegrationTests
{
    private static readonly string FixturesDir = Path.GetFullPath(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "fixtures", "integration"));

    private static readonly string HarnessProject = Path.GetFullPath(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "tools", "ConsistencyHarness", "ConsistencyHarness.csproj"));

    private string _ns20OutputDir = null!;

    private static readonly string[] Fixtures =
    [
        "deeply-nested-lists",
        "complex-tables",
        "conditionals-and-attributes",
        "inline-formatting",
        "admonitions-and-blocks",
        "large-document",
        "special-characters",
        "long-paragraphs",
        "anchors-and-xrefs",
    ];

    [OneTimeSetUp]
    public void Setup()
    {
        _ns20OutputDir = Path.Combine(Path.GetTempPath(), "adocnet-integration-ns20");
        Directory.CreateDirectory(_ns20OutputDir);

        // Generate ns2.0 output via harness
        var psi = new ProcessStartInfo("dotnet")
        {
            Arguments = $"run --project \"{HarnessProject}\" -- \"{FixturesDir}\" \"{_ns20OutputDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        var proc = Process.Start(psi)!;
        proc.WaitForExit(120_000);
        if (proc.ExitCode != 0)
        {
            var stderr = proc.StandardError.ReadToEnd();
            Assert.Fail($"ConsistencyHarness failed: {stderr}");
        }
    }

    [TestCaseSource(nameof(Fixtures))]
    public void CrossTfm_HtmlOutput_IsIdentical(string fixture)
    {
        var adocPath = Path.Combine(FixturesDir, fixture + ".adoc");
        var text = File.ReadAllText(adocPath);

        // net10.0 output (direct)
        var result = AdocParser.Parse(text);
        var net10Html = new HtmlRenderer().RenderToString(result.Document);

        // ns2.0 output (from harness)
        var ns20Html = File.ReadAllText(Path.Combine(_ns20OutputDir, fixture + ".html"));

        Assert.That(ns20Html, Is.EqualTo(net10Html),
            $"Cross-TFM HTML differs for {fixture}.adoc");
    }

    [TestCaseSource(nameof(Fixtures))]
    public void CrossTfm_AstOutput_IsIdentical(string fixture)
    {
        var adocPath = Path.Combine(FixturesDir, fixture + ".adoc");
        var text = File.ReadAllText(adocPath);

        // net10.0 output (direct)
        var result = AdocParser.Parse(text);
        var net10Ast = AstPrettyPrinter.Print(result.Document);

        // ns2.0 output (from harness)
        var ns20Ast = File.ReadAllText(Path.Combine(_ns20OutputDir, fixture + ".ast.txt"));

        Assert.That(ns20Ast, Is.EqualTo(net10Ast),
            $"Cross-TFM AST differs for {fixture}.adoc");
    }

    [TestCaseSource(nameof(Fixtures))]
    public void ParsesWithoutErrors(string fixture)
    {
        var adocPath = Path.Combine(FixturesDir, fixture + ".adoc");
        var text = File.ReadAllText(adocPath);
        var result = AdocParser.Parse(text);

        Assert.That(result.Document, Is.Not.Null);
        Assert.That(result.Document.Kind, Is.EqualTo(AstNodeKind.Document));
        // Allow warnings but no errors
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.That(errors, Is.Empty, $"Parse errors in {fixture}.adoc: {string.Join("; ", errors.Select(e => e.Message))}");
    }

    [TestCaseSource(nameof(Fixtures))]
    public void HtmlOutput_RendersSuccessfully(string fixture)
    {
        var adocPath = Path.Combine(FixturesDir, fixture + ".adoc");
        var text = File.ReadAllText(adocPath);
        var result = AdocParser.Parse(text);
        var html = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(html, Is.Not.Null.And.Not.Empty);
        Assert.That(html.Length, Is.GreaterThan(10),
            $"HTML output too short for {fixture}.adoc");
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        try { Directory.Delete(_ns20OutputDir, true); } catch { }
    }
}
