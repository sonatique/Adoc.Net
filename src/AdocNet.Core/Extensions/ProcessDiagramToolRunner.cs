using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace AdocNet.Extensions;

/// <summary>
/// Runs an external diagram tool via <c>Process.Start</c>.
/// Writes diagram source to a temp file, invokes the tool, and returns the output image path.
/// </summary>
public sealed class ProcessDiagramToolRunner : IDiagramToolRunner
{
    private readonly string _executablePath;
    private readonly string _arguments;

    /// <summary>
    /// Initializes the runner with the path to the diagram tool executable.
    /// </summary>
    /// <param name="executablePath">Path to the tool (e.g., "plantuml" or "/usr/bin/mmdc").</param>
    /// <param name="arguments">
    /// Additional arguments template. Use <c>{input}</c> and <c>{output}</c> as placeholders
    /// for the input and output file paths (e.g., "-tpng {input} -o {output}").
    /// </param>
    public ProcessDiagramToolRunner(string executablePath, string arguments = "-tpng {input} -o {output}")
    {
        _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
        _arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
    }

    private bool? _isAvailable;

    /// <inheritdoc />
    /// <remarks>Runs the tool with <c>--version</c> once; the answer is kept for the life of the runner.</remarks>
    public bool IsAvailable
    {
        get
        {
            if (_isAvailable is { } known)
                return known;
            try
            {
                var info = new ProcessStartInfo(_executablePath, "--version")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(info);
                return (_isAvailable = proc is not null && Finish(proc, 5000) && proc.ExitCode == 0).Value;
            }
            catch
            {
                return (_isAvailable = false).Value;
            }
        }
    }

    /// <summary>Waits for the process; kills it when it outlives <paramref name="milliseconds"/>.</summary>
    private static bool Finish(Process proc, int milliseconds)
    {
        if (proc.WaitForExit(milliseconds))
            return true;
        try { proc.Kill(); } catch (InvalidOperationException) { } // exited meanwhile
        return false;
    }

    /// <inheritdoc />
    public string? Generate(string language, string source, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        // Deterministic filename from source hash
        var hash = ComputeHash(language + ":" + source);
        var inputPath = Path.Combine(outputDirectory, $"{hash}.{language}");
        var outputPath = Path.Combine(outputDirectory, $"{hash}.png");

        if (File.Exists(outputPath))
            return outputPath; // made earlier from the same language and source

        File.WriteAllText(inputPath, source, Encoding.UTF8);

        // Substitute placeholders per token and pass arguments individually so that input/output
        // paths containing spaces (e.g. "C:\Users\John Smith\...") are not split into separate
        // arguments. A single concatenated, unquoted Arguments string would break such paths.
        var tokens = _arguments.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        // Output is not redirected: an unread pipe that fills up would hang the tool.
        var startInfo = new ProcessStartInfo(_executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = outputDirectory,
        };

#if NETSTANDARD2_0
        // netstandard2.0 has no ArgumentList; quote each substituted token defensively.
        startInfo.Arguments = string.Join(" ", Array.ConvertAll(tokens, t =>
        {
            var v = t.Replace("{input}", inputPath).Replace("{output}", outputPath);
            return v.IndexOf(' ') >= 0 && !(v.StartsWith("\"") && v.EndsWith("\"")) ? "\"" + v + "\"" : v;
        }));
#else
        foreach (var token in tokens)
            startInfo.ArgumentList.Add(token.Replace("{input}", inputPath).Replace("{output}", outputPath));
#endif

        using var proc = Process.Start(startInfo);
        if (proc is null) return null;

        if (!Finish(proc, 30_000) || proc.ExitCode != 0) return null;

        return File.Exists(outputPath) ? outputPath : null;
    }

    private static string ComputeHash(string input)
    {
#if NET5_0_OR_GREATER
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
#else
        using var sha = SHA256.Create();
        var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
#endif
        return BitConverter.ToString(bytes, 0, 8).Replace("-", "").ToLowerInvariant();
    }
}
