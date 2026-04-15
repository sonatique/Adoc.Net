using System.Diagnostics;

namespace AdocNet.Tools.DifferentialTester;

/// <summary>
/// Shells out to the Asciidoctor CLI to generate reference HTML output.
/// </summary>
public static class AsciidoctorRunner
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly bool IsWindows = OperatingSystem.IsWindows();
    private static string? _resolvedPath;

    /// <summary>
    /// Creates a ProcessStartInfo that works on both Windows (via cmd.exe for .bat resolution)
    /// and Unix (direct execution).
    /// </summary>
    private static ProcessStartInfo CreateStartInfo(string arguments)
    {
        if (!IsWindows)
            return new ProcessStartInfo
            {
                FileName = "asciidoctor",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

        var resolved = ResolveWindowsPath();
        return new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c {resolved} {arguments}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
    }

    /// <summary>
    /// Resolves the path to asciidoctor.bat on Windows, checking the system PATH
    /// and well-known Ruby installation directories.
    /// </summary>
    private static string ResolveWindowsPath()
    {
        if (_resolvedPath is not null)
            return _resolvedPath;

        // Check PATH entries first
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var batPath = Path.Combine(dir.Trim(), "asciidoctor.bat");
            if (File.Exists(batPath))
                return _resolvedPath = batPath;
        }

        // Check well-known Ruby installation directories
        string[] roots = [@"C:\tools", @"C:\"];
        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var dir in Directory.GetDirectories(root, "ruby*"))
                {
                    var batPath = Path.Combine(dir, "bin", "asciidoctor.bat");
                    if (File.Exists(batPath))
                        return _resolvedPath = batPath;
                }
            }
            catch { /* permission denied — skip */ }
        }

        // Fallback — let cmd.exe try to resolve it
        return _resolvedPath = "asciidoctor";
    }

    /// <summary>
    /// Checks whether the <c>asciidoctor</c> CLI is available on the system PATH.
    /// </summary>
    public static bool IsAvailable()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = CreateStartInfo("--version");
            process.Start();
            process.WaitForExit((int)DefaultTimeout.TotalMilliseconds);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the installed Asciidoctor version string, or null if not available.
    /// </summary>
    public static string? GetVersion()
    {
        try
        {
            using var process = new Process();
            process.StartInfo = CreateStartInfo("--version");
            process.Start();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit((int)DefaultTimeout.TotalMilliseconds);
            return process.ExitCode == 0 ? output.Trim().Split('\n')[0] : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Renders an AsciiDoc file to HTML using Asciidoctor.
    /// Uses <c>-s</c> (standalone fragment, no document wrapper) and <c>-o -</c> (stdout).
    /// Returns null if Asciidoctor is not available or the process fails.
    /// </summary>
    public static AsciidoctorResult? Render(string adocFilePath, TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? DefaultTimeout;

        try
        {
            var fullPath = Path.GetFullPath(adocFilePath);
            if (!File.Exists(fullPath))
                return null;

            using var process = new Process();
            // -s = standalone (no header/footer wrapper)
            // -o - = output to stdout
            process.StartInfo = CreateStartInfo($"-s -o - \"{fullPath}\"");
            process.StartInfo.WorkingDirectory = Path.GetDirectoryName(fullPath) ?? ".";

            process.Start();

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();

            bool exited = process.WaitForExit((int)effectiveTimeout.TotalMilliseconds);

            if (!exited)
            {
                try { process.Kill(); } catch { /* best effort */ }
                return new AsciidoctorResult(null, stderr, TimedOut: true);
            }

            if (process.ExitCode != 0)
                return new AsciidoctorResult(null, stderr, TimedOut: false);

            return new AsciidoctorResult(stdout, stderr, TimedOut: false);
        }
        catch (Exception ex)
        {
            return new AsciidoctorResult(null, ex.Message, TimedOut: false);
        }
    }
}

/// <summary>
/// Result of an Asciidoctor rendering operation.
/// </summary>
/// <param name="Html">The rendered HTML, or null if rendering failed.</param>
/// <param name="Stderr">Any stderr output (warnings, errors).</param>
/// <param name="TimedOut">Whether the process exceeded the timeout.</param>
public sealed record AsciidoctorResult(string? Html, string Stderr, bool TimedOut);
