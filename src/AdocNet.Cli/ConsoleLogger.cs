namespace AdocNet.Cli;

internal sealed class ConsoleLogger(TextWriter stdout, TextWriter stderr, bool verbose, bool quiet)
{
    public void LogSuccess(string inputPath, string outputPath, TimeSpan elapsed)
    {
        if (!verbose) return;
        stdout.WriteLine($"  [OK]   {inputPath} -> {outputPath} ({elapsed.TotalMilliseconds:F0}ms)");
    }

    public void LogFailure(string inputPath, string message)
    {
        stderr.WriteLine($"  [FAIL] {inputPath} -> {message}");
    }

    public void LogSummary(int total, int succeeded, int failed)
    {
        if (quiet && failed == 0) return;
        var failText = failed > 0 ? $" ({failed} failed)" : "";
        stdout.WriteLine($"Converted {succeeded}/{total} files{failText}");
    }

    public void LogInfo(string message)
    {
        if (!quiet) stdout.WriteLine(message);
    }

    public void LogDiagnostic(string message)
    {
        if (!quiet) stderr.WriteLine(message);
    }
}
