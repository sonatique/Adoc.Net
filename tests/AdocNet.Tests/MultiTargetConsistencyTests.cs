using System.Diagnostics;
using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;

namespace AdocNet.Tests;

/// <summary>
/// Validates behavioral consistency between netstandard2.0 and net10.0 builds.
/// The test generates output from the net10.0 build (direct API calls) and the
/// netstandard2.0 build (via the ConsistencyHarness targeting net8.0), then
/// compares AST and HTML output for each fixture file.
/// </summary>
[TestFixture]
public class MultiTargetConsistencyTests
{
    private static readonly string FixturesDir = Path.GetFullPath(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "fixtures", "multitarget"));

    private static readonly string HarnessProject = Path.GetFullPath(
        Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "tools", "ConsistencyHarness", "ConsistencyHarness.csproj"));

    private string _ns20OutputDir = null!;
    private string _net10OutputDir = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        _ns20OutputDir = Path.Combine(Path.GetTempPath(), "adocnet-consistency-ns20");
        _net10OutputDir = Path.Combine(Path.GetTempPath(), "adocnet-consistency-net10");

        Directory.CreateDirectory(_ns20OutputDir);
        Directory.CreateDirectory(_net10OutputDir);

        // Generate net10.0 output (direct API)
        var htmlRenderer = new HtmlRenderer();
        foreach (var file in Directory.GetFiles(FixturesDir, "*.adoc"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var text = File.ReadAllText(file);
            var result = AdocParser.Parse(text);

            File.WriteAllText(Path.Combine(_net10OutputDir, name + ".ast.txt"),
                AstPrettyPrinter.Print(result.Document));
            File.WriteAllText(Path.Combine(_net10OutputDir, name + ".html"),
                htmlRenderer.RenderToString(result.Document));

            try
            {
                var pdfRenderer = new AdocNet.Converters.Pdf.PdfRenderer();
                var pdfBytes = pdfRenderer.RenderToBytes(result.Document);
                int objectCount = System.Text.RegularExpressions.Regex.Matches(
                    Encoding.Latin1.GetString(pdfBytes), "endobj").Count;
                File.WriteAllText(Path.Combine(_net10OutputDir, name + ".pdf-info.txt"),
                    $"Objects={objectCount}");
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(_net10OutputDir, name + ".pdf-info.txt"),
                    $"Error={ex.GetType().Name}: {ex.Message}");
            }
        }

        // Generate ns2.0 output (via harness)
        var psi = new ProcessStartInfo("dotnet")
        {
            Arguments = $"run --project \"{HarnessProject}\" -- \"{FixturesDir}\" \"{_ns20OutputDir}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        var proc = Process.Start(psi)!;
        proc.WaitForExit(60_000);
        if (proc.ExitCode != 0)
        {
            var stderr = proc.StandardError.ReadToEnd();
            Assert.Fail($"ConsistencyHarness (ns2.0) failed with exit code {proc.ExitCode}:\n{stderr}");
        }
    }

    [TestCase("basic")]
    [TestCase("tables")]
    [TestCase("lists")]
    [TestCase("code-blocks")]
    [TestCase("unicode")]
    public void AstOutput_IsIdentical(string fixture)
    {
        var net10 = File.ReadAllText(Path.Combine(_net10OutputDir, fixture + ".ast.txt"));
        var ns20 = File.ReadAllText(Path.Combine(_ns20OutputDir, fixture + ".ast.txt"));
        Assert.That(ns20, Is.EqualTo(net10), $"AST output differs for {fixture}.adoc");
    }

    [TestCase("basic")]
    [TestCase("tables")]
    [TestCase("lists")]
    [TestCase("code-blocks")]
    [TestCase("unicode")]
    public void HtmlOutput_IsIdentical(string fixture)
    {
        var net10 = File.ReadAllText(Path.Combine(_net10OutputDir, fixture + ".html"));
        var ns20 = File.ReadAllText(Path.Combine(_ns20OutputDir, fixture + ".html"));
        Assert.That(ns20, Is.EqualTo(net10), $"HTML output differs for {fixture}.adoc");
    }

    [TestCase("basic")]
    [TestCase("tables")]
    [TestCase("lists")]
    [TestCase("code-blocks")]
    [TestCase("unicode")]
    public void PdfOutput_IsStructurallyEquivalent(string fixture)
    {
        var net10Info = File.ReadAllText(Path.Combine(_net10OutputDir, fixture + ".pdf-info.txt")).Trim();
        var ns20Info = File.ReadAllText(Path.Combine(_ns20OutputDir, fixture + ".pdf-info.txt")).Trim();

        // Both should either succeed or fail
        Assert.That(ns20Info.StartsWith("Error") == net10Info.StartsWith("Error"),
            Is.True, $"PDF status differs for {fixture}.adoc: net10={net10Info}, ns20={ns20Info}");

        if (!net10Info.StartsWith("Error"))
        {
            // Compare PDF object counts — a structural check. (Total byte size is not
            // comparable across runtimes: embedded fonts are Flate-compressed and
            // Deflate output differs between .NET runtimes.)
            Assert.That(ns20Info, Is.EqualTo(net10Info),
                $"PDF object count differs for {fixture}.adoc (may indicate structural difference)");
        }
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        // Cleanup
        try { Directory.Delete(_ns20OutputDir, true); } catch { }
        try { Directory.Delete(_net10OutputDir, true); } catch { }
    }
}
