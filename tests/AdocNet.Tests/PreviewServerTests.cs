using System.Net;
using System.Net.Sockets;
using AdocNet.Cli;

namespace AdocNet.Tests;

[TestFixture]
public class PreviewServerTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "adocnet-preview-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    internal static int GetFreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    [Test]
    public async Task Serves_html_file_from_root_directory()
    {
        var html = "<html><body><p>Hello World</p></body></html>";
        File.WriteAllText(Path.Combine(_tempDir, "test.html"), html);

        int port = GetFreePort();
        using var server = new PreviewServer(_tempDir, port);
        using var cts = new CancellationTokenSource();
        var serverTask = server.Run(cts.Token);
        await Task.Delay(200);

        using var client = new HttpClient();
        var response = await client.GetStringAsync($"http://localhost:{port}/test.html");

        Assert.That(response, Does.Contain("Hello World"));

        await cts.CancelAsync();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Test]
    public async Task Serves_index_page_for_root()
    {
        File.WriteAllText(Path.Combine(_tempDir, "alpha.html"), "<html><body></body></html>");
        File.WriteAllText(Path.Combine(_tempDir, "beta.html"), "<html><body></body></html>");

        int port = GetFreePort();
        using var server = new PreviewServer(_tempDir, port);
        using var cts = new CancellationTokenSource();
        var serverTask = server.Run(cts.Token);
        await Task.Delay(200);

        using var client = new HttpClient();
        var response = await client.GetStringAsync($"http://localhost:{port}/");

        Assert.That(response, Does.Contain("alpha.html"));
        Assert.That(response, Does.Contain("beta.html"));

        await cts.CancelAsync();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Test]
    public async Task Returns_404_for_missing_file()
    {
        int port = GetFreePort();
        using var server = new PreviewServer(_tempDir, port);
        using var cts = new CancellationTokenSource();
        var serverTask = server.Run(cts.Token);
        await Task.Delay(200);

        using var client = new HttpClient();
        var response = await client.GetAsync($"http://localhost:{port}/nonexistent.html");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

        await cts.CancelAsync();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Test]
    public async Task Injects_reload_script_into_html()
    {
        var html = "<html><body><p>Content</p></body></html>";
        File.WriteAllText(Path.Combine(_tempDir, "page.html"), html);

        int port = GetFreePort();
        using var server = new PreviewServer(_tempDir, port);
        using var cts = new CancellationTokenSource();
        var serverTask = server.Run(cts.Token);
        await Task.Delay(200);

        using var client = new HttpClient();
        var response = await client.GetStringAsync($"http://localhost:{port}/page.html");

        Assert.That(response, Does.Contain("/__reload.js"));
        Assert.That(response, Does.Contain("<script src=\"/__reload.js\"></script></body>"));

        await cts.CancelAsync();
        try { await serverTask; } catch (OperationCanceledException) { }
    }

    [Test]
    public async Task Serves_reload_script()
    {
        int port = GetFreePort();
        using var server = new PreviewServer(_tempDir, port);
        using var cts = new CancellationTokenSource();
        var serverTask = server.Run(cts.Token);
        await Task.Delay(200);

        using var client = new HttpClient();
        var response = await client.GetStringAsync($"http://localhost:{port}/__reload.js");

        Assert.That(response, Does.Contain("WebSocket"));
        Assert.That(response, Does.Contain("reload"));

        await cts.CancelAsync();
        try { await serverTask; } catch (OperationCanceledException) { }
    }
}
