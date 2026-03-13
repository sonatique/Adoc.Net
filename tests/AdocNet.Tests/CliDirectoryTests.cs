using System.IO;
using AdocNet.Cli;

namespace AdocNet.Tests;

[TestFixture]
public class CliDirectoryTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "adocnet-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void ConvertDirectory_creates_html_alongside_source()
    {
        File.WriteAllText(Path.Combine(_tempDir, "one.adoc"), "= One\n\nHello.\n");
        File.WriteAllText(Path.Combine(_tempDir, "two.adoc"), "= Two\n\nWorld.\n");

        var logger = new ConsoleLogger(TextWriter.Null, TextWriter.Null, verbose: false, quiet: true);
        var cmd = new ConvertCommand(logger);
        var run = new CliArgs.Run(_tempDir, OutputPath: null, OutDir: null, DumpAst: false, Quiet: true);

        int exitCode = cmd.ConvertDirectory(run);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(File.Exists(Path.Combine(_tempDir, "one.html")), Is.True);
        Assert.That(File.Exists(Path.Combine(_tempDir, "two.html")), Is.True);
    }

    [Test]
    public void ConvertDirectory_with_out_dir_preserves_structure()
    {
        var subDir = Path.Combine(_tempDir, "subdir");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(_tempDir, "root.adoc"), "= Root\n\nHello.\n");
        File.WriteAllText(Path.Combine(subDir, "page.adoc"), "= Page\n\nNested.\n");

        var outDir = Path.Combine(_tempDir, "out");
        var logger = new ConsoleLogger(TextWriter.Null, TextWriter.Null, verbose: false, quiet: true);
        var cmd = new ConvertCommand(logger);
        var run = new CliArgs.Run(_tempDir, OutputPath: null, OutDir: outDir, DumpAst: false, Recursive: true, Quiet: true);

        int exitCode = cmd.ConvertDirectory(run);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(File.Exists(Path.Combine(outDir, "root.html")), Is.True);
        Assert.That(File.Exists(Path.Combine(outDir, "subdir", "page.html")), Is.True);
    }

    [Test]
    public void ConvertDirectory_non_recursive_skips_subdirectories()
    {
        var subDir = Path.Combine(_tempDir, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(_tempDir, "root.adoc"), "= Root\n\nHello.\n");
        File.WriteAllText(Path.Combine(subDir, "nested.adoc"), "= Nested\n\nDeep.\n");

        var logger = new ConsoleLogger(TextWriter.Null, TextWriter.Null, verbose: false, quiet: true);
        var cmd = new ConvertCommand(logger);
        var run = new CliArgs.Run(_tempDir, OutputPath: null, OutDir: null, DumpAst: false, Recursive: false, Quiet: true);

        int exitCode = cmd.ConvertDirectory(run);

        Assert.That(exitCode, Is.EqualTo(0));
        Assert.That(File.Exists(Path.Combine(_tempDir, "root.html")), Is.True);
        Assert.That(File.Exists(Path.Combine(subDir, "nested.html")), Is.False);
    }

    [Test]
    public void ConvertDirectory_empty_dir_returns_success()
    {
        var logger = new ConsoleLogger(TextWriter.Null, TextWriter.Null, verbose: false, quiet: true);
        var cmd = new ConvertCommand(logger);
        var run = new CliArgs.Run(_tempDir, OutputPath: null, OutDir: null, DumpAst: false, Quiet: true);

        int exitCode = cmd.ConvertDirectory(run);

        Assert.That(exitCode, Is.EqualTo(0));
    }

    [Test]
    public void ConvertDirectory_returns_1_when_file_has_errors()
    {
        File.WriteAllText(Path.Combine(_tempDir, "bad.adoc"), "= Title\n\ninclude::nonexistent-file.adoc[]\n");

        var logger = new ConsoleLogger(TextWriter.Null, TextWriter.Null, verbose: false, quiet: true);
        var cmd = new ConvertCommand(logger);
        var run = new CliArgs.Run(_tempDir, OutputPath: null, OutDir: null, DumpAst: false, Quiet: true);

        int exitCode = cmd.ConvertDirectory(run);

        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(File.Exists(Path.Combine(_tempDir, "bad.html")), Is.True);
    }
}
