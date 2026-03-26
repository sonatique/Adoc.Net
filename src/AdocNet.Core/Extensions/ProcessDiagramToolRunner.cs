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

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
        {
            try
            {
                var info = new ProcessStartInfo(_executablePath, "--version")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(info);
                proc?.WaitForExit(5000);
                return proc is { ExitCode: 0 };
            }
            catch
            {
                return false;
            }
        }
    }

    /// <inheritdoc />
    public string? Generate(string language, string source, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);

        // Deterministic filename from source hash
        var hash = ComputeHash(source);
        var inputPath = Path.Combine(outputDirectory, $"{hash}.{language}");
        var outputPath = Path.Combine(outputDirectory, $"{hash}.png");

        File.WriteAllText(inputPath, source, Encoding.UTF8);

        var args = _arguments
            .Replace("{input}", inputPath)
            .Replace("{output}", outputPath);

        var startInfo = new ProcessStartInfo(_executablePath, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = outputDirectory,
        };

        using var proc = Process.Start(startInfo);
        if (proc is null) return null;

        proc.WaitForExit(30_000);
        if (proc.ExitCode != 0) return null;

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
