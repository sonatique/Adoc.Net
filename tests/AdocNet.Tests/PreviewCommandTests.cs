using System.Net.Http;
using AdocNet.Cli;

namespace AdocNet.Tests;

[TestFixture]
public class PreviewCommandTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "adocnet-prevcmd-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Test]
    public async Task Preview_builds_and_serves_adoc_file()
    {
        File.WriteAllText(Path.Combine(_tempDir, "hello.adoc"), "= Hello\n\nWorld.\n");

        var port = PreviewServerTests.GetFreePort();
        var args = new CliArgs.Preview(_tempDir, port, NoOpen: true);
        var logger = new ConsoleLogger(TextWriter.Null, TextWriter.Null, verbose: false, quiet: true);
        var cmd = new PreviewCommand(logger);

        using var cts = new CancellationTokenSource();
        var previewTask = Task.Run(() => cmd.Run(args, cts.Token));

        await Task.Delay(1000); // let build + server start

        using var client = new HttpClient();
        var html = await client.GetStringAsync($"http://localhost:{port}/hello.html");
        Assert.That(html, Does.Contain("Hello"));
        Assert.That(html, Does.Contain("World"));
        Assert.That(html, Does.Contain("/__reload.js"));

        cts.Cancel();
        try { await previewTask; } catch (OperationCanceledException) { }
    }

    [Test]
    public async Task Preview_index_lists_documents()
    {
        File.WriteAllText(Path.Combine(_tempDir, "one.adoc"), "= One\n\nFirst.\n");
        File.WriteAllText(Path.Combine(_tempDir, "two.adoc"), "= Two\n\nSecond.\n");

        var port = PreviewServerTests.GetFreePort();
        var args = new CliArgs.Preview(_tempDir, port, NoOpen: true);
        var logger = new ConsoleLogger(TextWriter.Null, TextWriter.Null, verbose: false, quiet: true);
        var cmd = new PreviewCommand(logger);

        using var cts = new CancellationTokenSource();
        var previewTask = Task.Run(() => cmd.Run(args, cts.Token));

        await Task.Delay(1000);

        using var client = new HttpClient();
        var index = await client.GetStringAsync($"http://localhost:{port}/");
        Assert.That(index, Does.Contain("one.html"));
        Assert.That(index, Does.Contain("two.html"));

        cts.Cancel();
        try { await previewTask; } catch (OperationCanceledException) { }
    }
}
