using System.Diagnostics;
using AdocNet.Ast;
using AdocNet.Converters.Html;
using AdocNet.Parser;
using NUnit.Framework;

namespace AdocNet.Tests;

[TestFixture]
public class CliTests
{
    private string _tempDir = null!;
    private string _cliProjectPath = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "adocnet-cli-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        // Walk up from test assembly to repo root
        var dir = TestContext.CurrentContext.TestDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "AdocNet.slnx")))
            dir = Path.GetDirectoryName(dir);
        Assert.That(dir, Is.Not.Null, "Could not find repo root");
        _cliProjectPath = Path.Combine(dir!, "src", "AdocNet.Cli", "AdocNet.Cli.csproj");
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private (int ExitCode, string StdOut, string StdErr) RunCli(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--no-build");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(
#if DEBUG
            "Debug"
#else
            "Release"
#endif
        );
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(_cliProjectPath);
        psi.ArgumentList.Add("--");
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(30_000);
        return (proc.ExitCode, stdout, stderr);
    }

    private string WriteTempAdoc(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ── Basic document processing ────────────────────────────────────────

    [Test]
    public void Simple_document_produces_html_on_stdout()
    {
        var input = WriteTempAdoc("basic.adoc", "= Title\n\nHello world.\n");
        var (exitCode, stdout, _) = RunCli(input);
        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdout, Does.Contain("<h1>Title</h1>"));
        Assert.That(stdout, Does.Contain("<p>Hello world.</p>"));
    }

    // ── HTML output matches library renderer ─────────────────────────────

    [Test]
    public void Html_output_matches_library_renderer()
    {
        var source = "= Doc\n\nA *bold* paragraph.\n";
        var input = WriteTempAdoc("match.adoc", source);
        var (exitCode, stdout, _) = RunCli(input);

        var result = AdocParser.Parse(source);
        var expected = new HtmlRenderer().RenderToString(result.Document);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdout, Is.EqualTo(expected));
    }

    // ── AST dump ─────────────────────────────────────────────────────────

    [Test]
    public void Dump_ast_prints_ast_instead_of_html()
    {
        var source = "= Title\n\nParagraph.\n";
        var input = WriteTempAdoc("ast.adoc", source);
        var (exitCode, stdout, _) = RunCli(input, "--dump-ast");

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdout, Does.Contain("Document"));
        Assert.That(stdout, Does.Contain("Paragraph"));
        Assert.That(stdout, Does.Not.Contain("<"));
    }

    // ── Output file ──────────────────────────────────────────────────────

    [Test]
    public void Output_file_option_writes_html_to_file()
    {
        var input = WriteTempAdoc("out.adoc", "Hello.\n");
        var outputPath = Path.Combine(_tempDir, "out.html");
        var (exitCode, stdout, _) = RunCli(input, "-o", outputPath);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdout, Is.Empty, "stdout should be empty when -o is used");
        Assert.That(File.Exists(outputPath), Is.True);
        var content = File.ReadAllText(outputPath);
        Assert.That(content, Does.Contain("<p>Hello.</p>"));
    }

    // ── Invalid arguments ────────────────────────────────────────────────

    [Test]
    public void No_arguments_produces_usage_error()
    {
        var (exitCode, _, stderr) = RunCli();
        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stderr, Does.Contain("No input file"));
        Assert.That(stderr, Does.Contain("Usage:"));
    }

    [Test]
    public void Unknown_option_produces_usage_error()
    {
        var (exitCode, _, stderr) = RunCli("--bad-option");
        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stderr, Does.Contain("Unknown option"));
    }

    [Test]
    public void Missing_input_file_produces_error()
    {
        var (exitCode, _, stderr) = RunCli("nonexistent.adoc");
        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(stderr, Does.Contain("file not found"));
    }

    // ── Help ─────────────────────────────────────────────────────────────

    [Test]
    public void Help_flag_shows_usage()
    {
        var (exitCode, stdout, _) = RunCli("--help");
        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdout, Does.Contain("Usage:"));
        Assert.That(stdout, Does.Contain("--dump-ast"));
        Assert.That(stdout, Does.Contain("-o"));
    }

    // ── Include support ──────────────────────────────────────────────────

    [Test]
    public void Includes_are_resolved_relative_to_input_file()
    {
        var partialPath = Path.Combine(_tempDir, "_partial.adoc");
        File.WriteAllText(partialPath, "Included content.\n");
        var input = WriteTempAdoc("main.adoc", "= Doc\n\ninclude::_partial.adoc[]\n");
        var (exitCode, stdout, _) = RunCli(input);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(stdout, Does.Contain("Included content."));
    }

    // ── Directory conversion ─────────────────────────────────────────────

    [Test]
    public void Directory_conversion_produces_html_files()
    {
        var inputDir = Path.Combine(_tempDir, "dir-convert-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(inputDir);
        File.WriteAllText(Path.Combine(inputDir, "one.adoc"), "= One\n\nFirst.\n");
        File.WriteAllText(Path.Combine(inputDir, "two.adoc"), "= Two\n\nSecond.\n");

        var (exitCode, stdout, _) = RunCli(inputDir, "-v");

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(File.Exists(Path.Combine(inputDir, "one.html")), Is.True);
        Assert.That(File.Exists(Path.Combine(inputDir, "two.html")), Is.True);
        Assert.That(stdout, Does.Contain("Converted 2/2"));
    }

    [Test]
    public void Directory_with_out_dir_creates_output_structure()
    {
        var inputDir = Path.Combine(_tempDir, "dir-outdir-" + Guid.NewGuid().ToString("N")[..8]);
        var outputDir = Path.Combine(_tempDir, "out-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(inputDir);
        File.WriteAllText(Path.Combine(inputDir, "doc.adoc"), "= Doc\n\nContent.\n");

        var (exitCode, stdout, _) = RunCli(inputDir, "--out-dir", outputDir);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(File.Exists(Path.Combine(outputDir, "doc.html")), Is.True);
        Assert.That(stdout, Does.Contain("Converted 1/1"));
    }

    // ── Config file integration ─────────────────────────────────────────

    [Test]
    public void Config_file_sets_output_directory()
    {
        var inputDir = Path.Combine(_tempDir, "project-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(inputDir);
        File.WriteAllText(Path.Combine(inputDir, "doc.adoc"), "= Title\n\nHello.\n");
        File.WriteAllText(Path.Combine(inputDir, "adocnet.json"),
            """{"outDir": "out"}""");

        var (exitCode, _, _) = RunCli(inputDir, "-v");

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(File.Exists(Path.Combine(inputDir, "out", "doc.html")), Is.True);
    }
}
