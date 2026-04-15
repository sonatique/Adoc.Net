using System.IO;
using AdocNet.Cli;

namespace AdocNet.Tests;

[TestFixture]
public class WatchCommandTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "adocnet-watch-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public void RunInitialBuild_converts_directory()
    {
        File.WriteAllText(Path.Combine(_tempDir, "test.adoc"), "= Title\n\nHello.\n");

        var logger = new ConsoleLogger(TextWriter.Null, TextWriter.Null, verbose: false, quiet: true);
        var command = new ConvertCommand(logger);
        var watch = new WatchCommand(command, logger);
        var run = new CliArgs.Run(_tempDir, OutputPath: null, OutDir: null, DumpAst: false, Quiet: true, Watch: true);

        int result = watch.RunInitialBuild(run);

        Assert.That(result, Is.EqualTo(0));
        Assert.That(File.Exists(Path.Combine(_tempDir, "test.html")), Is.True);
    }

    [Test]
    public void RebuildFile_updates_output()
    {
        var adocPath = Path.Combine(_tempDir, "test.adoc");
        File.WriteAllText(adocPath, "= Original\n\nFirst.\n");

        var logger = new ConsoleLogger(TextWriter.Null, TextWriter.Null, verbose: false, quiet: true);
        var command = new ConvertCommand(logger);
        var watch = new WatchCommand(command, logger);
        var run = new CliArgs.Run(_tempDir, OutputPath: null, OutDir: null, DumpAst: false, Quiet: true, Watch: true);

        watch.RunInitialBuild(run);
        var htmlPath = Path.Combine(_tempDir, "test.html");
        // Title is suppressed in embedded mode; check for body content instead.
        Assert.That(File.ReadAllText(htmlPath), Does.Contain("First."));

        // Simulate edit
        File.WriteAllText(adocPath, "= Updated\n\nSecond.\n");
        watch.RebuildFile(adocPath, run);

        Assert.That(File.ReadAllText(htmlPath), Does.Contain("Second."));
    }

    [Test]
    public void RunInitialBuild_single_file()
    {
        var adocPath = Path.Combine(_tempDir, "single.adoc");
        File.WriteAllText(adocPath, "= Hello\n\nWorld.\n");

        var logger = new ConsoleLogger(TextWriter.Null, TextWriter.Null, verbose: false, quiet: true);
        var command = new ConvertCommand(logger);
        var watch = new WatchCommand(command, logger);
        var run = new CliArgs.Run(adocPath, OutputPath: null, OutDir: null, DumpAst: false, Quiet: true, Watch: true);

        int result = watch.RunInitialBuild(run);

        Assert.That(result, Is.EqualTo(0));
        Assert.That(File.Exists(Path.Combine(_tempDir, "single.html")), Is.True);
    }
}
