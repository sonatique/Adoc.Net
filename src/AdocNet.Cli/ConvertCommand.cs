using System.Diagnostics;
using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.DocBook;
using AdocNet.Converters.Epub;
using AdocNet.Converters.Html;
using AdocNet.Converters.Pdf;
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

        switch (run.Format)
        {
            case OutputFormat.Html:
            {
                var renderer = new HtmlRenderer();
                var htmlOptions = run.Styled
                    ? new HtmlRenderOptions { Theme = run.Theme, FullDocument = true }
                    : HtmlRenderOptions.Default;
                using var ms = new MemoryStream();
                renderer.Render(document, ms, htmlOptions);
                string htmlOutput = Encoding.UTF8.GetString(ms.ToArray());
                WriteTextOutput(effectiveOutputPath, htmlOutput);
                break;
            }

            case OutputFormat.Pdf:
            {
                var pdfRenderer = new PdfRenderer();
                using var pdfStream = new MemoryStream();
                pdfRenderer.Render(document, pdfStream, PdfRenderOptions.Default);
                WriteBinaryOutput(effectiveOutputPath, pdfStream.ToArray());
                break;
            }

            case OutputFormat.DocBook:
            {
                var renderer = new DocBookRenderer();
                using var ms = new MemoryStream();
                renderer.Render(document, ms, RenderOptions.Default);
                string output = Encoding.UTF8.GetString(ms.ToArray());
                WriteTextOutput(effectiveOutputPath, output);
                break;
            }

            case OutputFormat.Epub:
            {
                var renderer = new EpubRenderer();
                using var ms = new MemoryStream();
                renderer.Render(document, ms, RenderOptions.Default);
                WriteBinaryOutput(effectiveOutputPath, ms.ToArray());
                break;
            }
        }
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
