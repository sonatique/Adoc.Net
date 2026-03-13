namespace AdocNet.Cli;

internal sealed class WatchCommand(ConvertCommand convertCommand, ConsoleLogger logger)
{
    private const int DebounceMs = 300;

    /// <summary>Run the initial build (same as non-watch mode).</summary>
    public int RunInitialBuild(CliArgs.Run run)
    {
        if (Directory.Exists(run.InputPath))
            return convertCommand.ConvertDirectory(run);

        // In watch mode for a single file, always write to a file (not stdout)
        var outputPath = run.OutputPath ?? Path.ChangeExtension(run.InputPath, FormatExtension(run.Format));
        return convertCommand.ConvertFile(run.InputPath, outputPath, run);
    }

    /// <summary>Rebuild a single changed file.</summary>
    public void RebuildFile(string filePath, CliArgs.Run run)
    {
        // Determine output path based on whether we're watching a directory or single file
        string? outputPath;
        if (Directory.Exists(run.InputPath))
        {
            // Directory mode: resolve output path relative to input dir
            var ext = FormatExtension(run.Format);
            if (run.OutDir is not null)
            {
                var relative = Path.GetRelativePath(run.InputPath, filePath);
                outputPath = Path.Combine(run.OutDir, Path.ChangeExtension(relative, ext));
            }
            else
            {
                outputPath = Path.ChangeExtension(filePath, ext);
            }
        }
        else
        {
            // Single file mode: use -o or replace extension
            outputPath = run.OutputPath ?? Path.ChangeExtension(filePath, FormatExtension(run.Format));
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (dir is not null) Directory.CreateDirectory(dir);
        convertCommand.ConvertFile(filePath, outputPath, run);
    }

    /// <summary>Run watch loop: initial build, then watch for changes until cancellation.</summary>
    public int Run(CliArgs.Run run, CancellationToken cancellation)
    {
        int initialResult = RunInitialBuild(run);

        var watchPath = Directory.Exists(run.InputPath)
            ? Path.GetFullPath(run.InputPath)
            : Path.GetDirectoryName(Path.GetFullPath(run.InputPath))!;

        logger.LogInfo($"Watching for changes in {watchPath}... (press Ctrl+C to stop)");

        using var watcher = new FileSystemWatcher(watchPath, "*.adoc")
        {
            IncludeSubdirectories = run.Recursive,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
        };

        var debounceTimers = new Dictionary<string, DateTime>();

        watcher.Changed += (_, e) => HandleChange(e.FullPath, run, debounceTimers);
        watcher.Created += (_, e) => HandleChange(e.FullPath, run, debounceTimers);
        watcher.Renamed += (_, e) => HandleChange(e.FullPath, run, debounceTimers);
        watcher.EnableRaisingEvents = true;

        cancellation.WaitHandle.WaitOne();
        return initialResult;
    }

    private void HandleChange(string filePath, CliArgs.Run run, Dictionary<string, DateTime> debounce)
    {
        var now = DateTime.UtcNow;
        lock (debounce)
        {
            if (debounce.TryGetValue(filePath, out var last) && (now - last).TotalMilliseconds < DebounceMs)
                return;
            debounce[filePath] = now;
        }

        Thread.Sleep(50); // let editor finish writing

        try
        {
            RebuildFile(filePath, run);
        }
        catch (Exception ex)
        {
            logger.LogFailure(filePath, ex.Message);
        }
    }

    private static string FormatExtension(OutputFormat format) => format switch
    {
        OutputFormat.Html => ".html",
        OutputFormat.Pdf => ".pdf",
        OutputFormat.DocBook => ".xml",
        OutputFormat.Epub => ".epub",
        _ => ".html",
    };
}
