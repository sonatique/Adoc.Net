using System.Net;
using System.Net.WebSockets;
using System.Text;

namespace AdocNet.Cli;

internal sealed class PreviewServer(string rootDir, int port) : IDisposable
{
    private static readonly string ReloadScript = """
        (function() {
          var ws = new WebSocket('ws://' + location.host + '/__ws');
          ws.onmessage = function(e) { if (e.data === 'reload') location.reload(); };
          ws.onclose = function() { setTimeout(function() { location.reload(); }, 2000); };
        })();
        """;

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".js"] = "application/javascript; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".svg"] = "image/svg+xml",
    };

    private readonly string _rootDir = Path.GetFullPath(rootDir);
    private readonly HttpListener _listener = new();
    private readonly Lock _wsLock = new();
    private readonly List<WebSocket> _sockets = [];

    public async Task Run(CancellationToken cancellation)
    {
        _listener.Prefixes.Add($"http://localhost:{port}/");
        _listener.Start();

        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var getContextTask = _listener.GetContextAsync();
                await getContextTask.WaitAsync(cancellation);
                var ctx = getContextTask.Result;
                _ = HandleRequestAsync(ctx, cancellation);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        finally
        {
            _listener.Stop();
        }
    }

    public void NotifyReload()
    {
        var message = Encoding.UTF8.GetBytes("reload");
        var segment = new ArraySegment<byte>(message);

        lock (_wsLock)
        {
            for (int i = _sockets.Count - 1; i >= 0; i--)
            {
                var ws = _sockets[i];
                if (ws.State != WebSocketState.Open)
                {
                    _sockets.RemoveAt(i);
                    ws.Dispose();
                    continue;
                }

                try
                {
                    ws.SendAsync(segment, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None)
                      .GetAwaiter().GetResult();
                }
                catch
                {
                    _sockets.RemoveAt(i);
                    ws.Dispose();
                }
            }
        }
    }

    public void Dispose()
    {
        _listener.Close();
        lock (_wsLock)
        {
            foreach (var ws in _sockets)
                ws.Dispose();
            _sockets.Clear();
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";

            if (path == "/__ws" && ctx.Request.IsWebSocketRequest)
            {
                await HandleWebSocket(ctx, ct);
                return;
            }

            if (path == "/__reload.js")
            {
                ServeString(ctx.Response, ReloadScript, "application/javascript; charset=utf-8");
                return;
            }

            if (path == "/")
            {
                ServeIndex(ctx.Response);
                return;
            }

            ServeFile(ctx.Response, path);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            // Shutting down
        }
        catch
        {
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); }
            catch { /* best effort */ }
        }
    }

    private async Task HandleWebSocket(HttpListenerContext ctx, CancellationToken ct)
    {
        var wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null);
        var ws = wsCtx.WebSocket;

        lock (_wsLock)
        {
            _sockets.Add(ws);
        }

        try
        {
            var buffer = new byte[256];
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        finally
        {
            lock (_wsLock)
            {
                _sockets.Remove(ws);
            }

            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "closing", CancellationToken.None);
                }
                catch { /* best effort */ }
            }

            ws.Dispose();
        }
    }

    private void ServeIndex(HttpListenerResponse response)
    {
        var htmlFiles = Directory.EnumerateFiles(_rootDir, "*.html", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_rootDir, f).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\">");
        sb.AppendLine("<title>Preview Index</title>");
        sb.AppendLine("<style>body{font-family:sans-serif;max-width:800px;margin:2em auto;padding:0 1em}" +
                      "a{display:block;padding:0.3em 0;color:#0366d6;text-decoration:none}" +
                      "a:hover{text-decoration:underline}" +
                      "h1{border-bottom:1px solid #eee;padding-bottom:0.3em}</style>");
        sb.AppendLine("</head><body><h1>Preview</h1><ul>");

        foreach (var file in htmlFiles)
        {
            sb.AppendLine($"<li><a href=\"/{file}\">{file}</a></li>");
        }

        sb.AppendLine("</ul></body></html>");
        ServeString(response, sb.ToString(), "text/html; charset=utf-8");
    }

    private void ServeFile(HttpListenerResponse response, string urlPath)
    {
        // Decode and sanitize path
        var decoded = Uri.UnescapeDataString(urlPath).TrimStart('/');
        var fullPath = Path.GetFullPath(Path.Combine(_rootDir, decoded));

        // Prevent directory traversal
        if (!fullPath.StartsWith(_rootDir, StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 403;
            response.Close();
            return;
        }

        if (!File.Exists(fullPath))
        {
            response.StatusCode = 404;
            response.Close();
            return;
        }

        var ext = Path.GetExtension(fullPath);
        var contentType = ContentTypes.GetValueOrDefault(ext, "application/octet-stream");
        response.ContentType = contentType;

        if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase))
        {
            // Inject reload script into HTML
            var html = File.ReadAllText(fullPath, Encoding.UTF8);
            html = html.Replace("</body>", "<script src=\"/__reload.js\"></script></body>");
            var bytes = Encoding.UTF8.GetBytes(html);
            response.ContentLength64 = bytes.Length;
            response.OutputStream.Write(bytes);
        }
        else
        {
            using var fs = File.OpenRead(fullPath);
            response.ContentLength64 = fs.Length;
            fs.CopyTo(response.OutputStream);
        }

        response.Close();
    }

    private static void ServeString(HttpListenerResponse response, string content, string contentType)
    {
        response.ContentType = contentType;
        var bytes = Encoding.UTF8.GetBytes(content);
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes);
        response.Close();
    }
}
