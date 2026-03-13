using System.Diagnostics;
using AdocNet.Converters.Html;

namespace AdocNet.Cli;

internal sealed class PreviewCommand(ConsoleLogger logger)
{
    public int Run(CliArgs.Preview args, CancellationToken cancellation)
    {
        // Create temp output directory
        var outputDir = Path.Combine(Path.GetTempPath(), "adocnet-preview-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(outputDir);

        try
        {
            // Initial build
            BuildAll(args, outputDir);

            // Start HTTP server
            using var server = new PreviewServer(outputDir, args.Port);
            var serverTask = Task.Run(() => server.Run(cancellation), cancellation);

            var url = $"http://localhost:{args.Port}/";
            logger.LogInfo($"Preview server running at {url}");

            // Auto-open browser
            if (!args.NoOpen)
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { /* ignore if browser can't open */ }
            }

            // Watch for changes
            var sourcePath = Path.GetFullPath(args.InputPath);
            var watchDir = Directory.Exists(sourcePath) ? sourcePath : Path.GetDirectoryName(sourcePath)!;

            using var watcher = new FileSystemWatcher(watchDir, "*.adoc")
            {
                IncludeSubdirectories = args.Recursive,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };

            var debounce = new Dictionary<string, DateTime>();
            void OnChange(string filePath)
            {
                var now = DateTime.UtcNow;
                lock (debounce)
                {
                    if (debounce.TryGetValue(filePath, out var last) && (now - last).TotalMilliseconds < 300)
                        return;
                    debounce[filePath] = now;
                }

                Thread.Sleep(50);
                try
                {
                    RebuildFile(filePath, args, outputDir);
                    server.NotifyReload();
                }
                catch (Exception ex)
                {
                    logger.LogFailure(filePath, ex.Message);
                }
            }

            watcher.Changed += (_, e) => OnChange(e.FullPath);
            watcher.Created += (_, e) => OnChange(e.FullPath);
            watcher.Renamed += (_, e) => OnChange(e.FullPath);
            watcher.EnableRaisingEvents = true;

            logger.LogInfo("Watching for changes... (press Ctrl+C to stop)");

            cancellation.WaitHandle.WaitOne();
            return 0;
        }
        finally
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }

    private void BuildAll(CliArgs.Preview args, string outputDir)
    {
        var sourcePath = Path.GetFullPath(args.InputPath);
        string[] files;
        string sourceDir;

        if (Directory.Exists(sourcePath))
        {
            var search = args.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            files = Directory.GetFiles(sourcePath, "*.adoc", search);
            sourceDir = sourcePath;
        }
        else
        {
            files = [sourcePath];
            sourceDir = Path.GetDirectoryName(sourcePath)!;
        }

        var quietLogger = new ConsoleLogger(TextWriter.Null, TextWriter.Null, verbose: false, quiet: true);
        var converter = new ConvertCommand(quietLogger);

        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var outPath = Path.Combine(outputDir, Path.ChangeExtension(relative, ".html"));
            var dir = Path.GetDirectoryName(outPath);
            if (dir is not null) Directory.CreateDirectory(dir);

            var run = new CliArgs.Run(file, outPath, null, false, OutputFormat.Html, args.Styled, args.Theme, false, false, true, false);
            converter.ConvertFile(file, outPath, run);
        }

        logger.LogInfo($"Built {files.Length} file(s) for preview.");
    }

    private static void RebuildFile(string filePath, CliArgs.Preview args, string outputDir)
    {
        var sourcePath = Path.GetFullPath(args.InputPath);
        var sourceDir = Directory.Exists(sourcePath) ? sourcePath : Path.GetDirectoryName(sourcePath)!;

        var relative = Path.GetRelativePath(sourceDir, filePath);
        var outPath = Path.Combine(outputDir, Path.ChangeExtension(relative, ".html"));
        var dir = Path.GetDirectoryName(outPath);
        if (dir is not null) Directory.CreateDirectory(dir);

        var quietLogger = new ConsoleLogger(TextWriter.Null, TextWriter.Null, verbose: false, quiet: true);
        var converter = new ConvertCommand(quietLogger);
        var run = new CliArgs.Run(filePath, outPath, null, false, OutputFormat.Html, args.Styled, args.Theme, false, false, true, false);
        converter.ConvertFile(filePath, outPath, run);
    }
}
