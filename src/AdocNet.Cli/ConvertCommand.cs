using System.Diagnostics;
using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.DocBook;
using AdocNet.Converters.Epub;
using AdocNet.Converters.Html;
using AdocNet.Converters.Pdf;
using AdocNet.Extensions;
using AdocNet.Parser;

namespace AdocNet.Cli;

internal sealed class ConvertCommand(ConsoleLogger logger)
{
    private const int ExitSuccess = 0;
    private const int ExitParseError = 1;
    private const int ExitUsageError = 2;

    public int ConvertFile(string inputPath, string? outputPath, CliArgs.Run options)
    {
        var sw = Stopwatch.StartNew();

        // ── Read input ───────────────────────────────────────────────────
        string sourceText;
        try
        {
            sourceText = File.ReadAllText(inputPath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            Console.Error.WriteLine($"Error: file not found: {inputPath}");
            return ExitUsageError;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Error: cannot read file: {inputPath} ({ex.Message})");
            return ExitUsageError;
        }

        // ── Parse ────────────────────────────────────────────────────────
        var parseOptions = new ParseOptions
        {
            SourceFilePath = Path.GetFullPath(inputPath),
            Attributes = options.Attributes,
            SafeMode = options.SafeMode,
        };

        var result = AdocParser.Parse(sourceText, parseOptions);

        // ── Diagnostics to stderr ────────────────────────────────────────
        bool hasErrors = false;
        foreach (var diag in result.Diagnostics)
        {
            logger.LogDiagnostic(FormatDiagnostic(diag));
            if (diag.IsError)
                hasErrors = true;
        }

        // ── Output ───────────────────────────────────────────────────────
        if (options.DumpAst)
        {
            string astOutput = AstPrettyPrinter.Print(result.Document);
            WriteTextOutput(outputPath, astOutput);
        }
        else
        {
            RenderOutput(options, result.Document, outputPath);
        }

        sw.Stop();
        if (outputPath is not null)
            logger.LogSuccess(inputPath, outputPath, sw.Elapsed);

        return hasErrors ? ExitParseError : ExitSuccess;
    }

    public int ConvertDirectory(CliArgs.Run run)
    {
        var searchOption = run.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var files = Directory.GetFiles(run.InputPath, "*.adoc", searchOption);
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);

        int succeeded = 0, failed = 0;
        foreach (var file in files)
        {
            var outputPath = ResolveOutputPath(file, run.InputPath, run.OutDir, run.Format);
            var dir = Path.GetDirectoryName(outputPath);
            if (dir is not null) Directory.CreateDirectory(dir);

            int result = ConvertFile(file, outputPath, run);
            if (result == 0) succeeded++; else failed++;
        }

        logger.LogSummary(files.Length, succeeded, failed);
        return failed > 0 ? 1 : 0;
    }

    private static string ResolveOutputPath(string inputFile, string inputDir, string? outDir, OutputFormat format)
    {
        var ext = FormatExtension(format);
        if (outDir is null)
            return Path.ChangeExtension(inputFile, ext);
        var relative = Path.GetRelativePath(inputDir, inputFile);
        return Path.Combine(outDir, Path.ChangeExtension(relative, ext));
    }

    private static string FormatExtension(OutputFormat format) => format switch
    {
        OutputFormat.Html => ".html",
        OutputFormat.Pdf => ".pdf",
        OutputFormat.DocBook => ".xml",
        OutputFormat.Epub => ".epub",
        _ => ".html",
    };

    internal static string FormatDiagnostic(Diagnostic diag)
    {
        var severity = diag.Severity switch
        {
            DiagnosticSeverity.Error => "Error",
            DiagnosticSeverity.Warning => "Warning",
            _ => "Info",
        };

        string location;
        if (diag.FilePath is not null && !diag.Range.IsNone)
            location = $" at {diag.FilePath}:{diag.Range.Start.Line}";
        else if (diag.FilePath is not null)
            location = $" at {diag.FilePath}";
        else if (!diag.Range.IsNone)
            location = $" at line {diag.Range.Start.Line}";
        else
            location = "";

        return $"{severity}: {diag.Message}{location}";
    }

    private static void RenderOutput(CliArgs.Run run, DocumentNode document, string? outputPath = null)
    {
        var effectiveOutputPath = outputPath ?? run.OutputPath;

        IDocumentRenderer renderer = run.Format switch
        {
            OutputFormat.Html => new HtmlRenderer(),
            OutputFormat.Pdf => new PdfRenderer(),
            OutputFormat.DocBook => new DocBookRenderer(),
            OutputFormat.Epub => new EpubRenderer(),
            _ => new HtmlRenderer(),
        };

        RenderOptions options = run.Format switch
        {
            OutputFormat.Html when run.Styled => new HtmlRenderOptions { Theme = run.Theme, FullDocument = true },
            OutputFormat.Html => HtmlRenderOptions.Default,
            OutputFormat.Pdf => PdfRenderOptions.Default,
            _ => RenderOptions.Default,
        };

        // Determine if any extensions should be loaded
        bool hasAutoExtensions = !run.NoAutoExtensions;
        bool hasExplicitExtensions = run.ExtensionPaths is { Count: > 0 } || run.ExtensionDirs is { Count: > 0 };

        if (hasAutoExtensions || hasExplicitExtensions)
        {
            var engine = new AdocEngine(renderer, _ => document);
            engine.OnWarning = msg => Console.Error.WriteLine($"Warning: {msg}");

            if (hasAutoExtensions)
                engine.LoadInstalledExtensions();

            LoadExtensions(engine, run);

            using var ms = new MemoryStream();
            engine.Convert("", ms, options);
            WriteOutput(effectiveOutputPath, ms.ToArray(), run.Format);
        }
        else
        {
            using var ms = new MemoryStream();
            renderer.Render(document, ms, options);
            WriteOutput(effectiveOutputPath, ms.ToArray(), run.Format);
        }
    }

    private static void LoadExtensions(AdocEngine engine, CliArgs.Run run)
    {
        if (run.ExtensionPaths is { Count: > 0 })
            foreach (var path in run.ExtensionPaths)
                engine.LoadExtension(path);

        if (run.ExtensionDirs is { Count: > 0 })
            foreach (var dir in run.ExtensionDirs)
                engine.LoadExtensions(dir);
    }

    private static void WriteOutput(string? outputPath, byte[] content, OutputFormat format)
    {
        bool isText = format is OutputFormat.Html or OutputFormat.DocBook;
        if (isText)
            WriteTextOutput(outputPath, Encoding.UTF8.GetString(content));
        else
            WriteBinaryOutput(outputPath, content);
    }

    private static void WriteTextOutput(string? outputPath, string content)
    {
        if (outputPath is not null)
        {
            try
            {
                File.WriteAllText(outputPath, content);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Error: cannot write file: {outputPath} ({ex.Message})");
            }
        }
        else
        {
            Console.Write(content);
        }
    }

    private static void WriteBinaryOutput(string? outputPath, byte[] content)
    {
        if (outputPath is not null)
        {
            try
            {
                File.WriteAllBytes(outputPath, content);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Error: cannot write file: {outputPath} ({ex.Message})");
            }
        }
        else
        {
            using var stdout = Console.OpenStandardOutput();
            stdout.Write(content, 0, content.Length);
        }
    }
}
