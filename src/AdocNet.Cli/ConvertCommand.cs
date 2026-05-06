using System.Diagnostics;
using System.Text;
using AdocNet.Ast;
using AdocNet.Converters.DocBook;
using AdocNet.Converters.Epub;
using AdocNet.Converters.Html;
using AdocNet.Converters.Man;
using AdocNet.Converters.Pdf;
using AdocNet.Converters.Revealjs;
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
        OutputFormat.Man => ".1",
        OutputFormat.Revealjs => ".html",
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
            OutputFormat.Man => new ManRenderer(),
            OutputFormat.Revealjs => new RevealjsRenderer(),
            _ => new HtmlRenderer(),
        };

        RenderOptions options = run.Format switch
        {
            OutputFormat.Html when run.Styled => new HtmlRenderOptions { Theme = run.Theme, FullDocument = true },
            OutputFormat.Html => HtmlRenderOptions.Default,
            OutputFormat.Pdf when run.PdfThemePath is not null => LoadPdfTheme(run),
            OutputFormat.Pdf => PdfRenderOptions.Default,
            _ => RenderOptions.Default,
        };

        // Auto-detect diagram blocks in the AST (plantuml, mermaid, ditaa, graphviz, dot)
        bool hasDiagramBlocks = run.RequireKroki || ContainsDiagramBlocks(document);

        // Determine if any extensions should be loaded
        bool hasAutoExtensions = !run.NoAutoExtensions;
        bool hasExplicitExtensions = run.ExtensionPaths is { Count: > 0 } || run.ExtensionDirs is { Count: > 0 };
        bool needsEngine = hasAutoExtensions || hasExplicitExtensions || hasDiagramBlocks;

        if (needsEngine)
        {
            var engine = new AdocEngine(renderer, _ => document);
            engine.OnWarning = msg => Console.Error.WriteLine($"Warning: {msg}");

            if (hasAutoExtensions)
                engine.LoadInstalledExtensions();

            if (hasDiagramBlocks)
            {
                var kroki = new KrokiDiagramToolRunner();
                var diagramDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(run.InputPath)) ?? ".", ".adocnet-diagrams");
                engine.RegisterBlockProcessor(new DiagramBlockProcessor(kroki, diagramDir));
            }

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

    private static PdfRenderOptions LoadPdfTheme(CliArgs.Run run)
    {
        try
        {
            // Resolve fontsDir: explicit --pdf-fontsdir flag, or --attribute pdf-fontsdir=... (Asciidoctor compat)
            var fontsDir = run.PdfFontsDir;
            if (fontsDir is null && run.Attributes is not null && run.Attributes.TryGetValue("pdf-fontsdir", out var attrFontsDir) && attrFontsDir.Length > 0)
                fontsDir = attrFontsDir;
            var options = PdfThemeLoader.Load(run.PdfThemePath!, fontsDir);
            // Override BaseDirectory relative to the input file for image resolution
            var inputDir = Path.GetDirectoryName(Path.GetFullPath(run.InputPath));
            return new PdfRenderOptions
            {
                FontPath = options.FontPath,
                BoldFontPath = options.BoldFontPath,
                ItalicFontPath = options.ItalicFontPath,
                MonoFontPath = options.MonoFontPath,
                MonoBoldFontPath = options.MonoBoldFontPath,
                MonoItalicFontPath = options.MonoItalicFontPath,
                MonoBoldItalicFontPath = options.MonoBoldItalicFontPath,
                HeadingFontPath = options.HeadingFontPath,
                PageWidth = options.PageWidth,
                PageHeight = options.PageHeight,
                MarginTop = options.MarginTop,
                MarginRight = options.MarginRight,
                MarginBottom = options.MarginBottom,
                MarginLeft = options.MarginLeft,
                FontSize = options.FontSize,
                CodeFontSize = options.CodeFontSize,
                TitleFontSize = options.TitleFontSize,
                LineSpacing = options.LineSpacing,
                TitleLineHeight = options.TitleLineHeight,
                Heading2FontSize = options.Heading2FontSize,
                Heading3FontSize = options.Heading3FontSize,
                Heading4FontSize = options.Heading4FontSize,
                Heading5FontSize = options.Heading5FontSize,
                Heading2MarginBottom = options.Heading2MarginBottom,
                Heading3MarginBottom = options.Heading3MarginBottom,
                Heading4MarginBottom = options.Heading4MarginBottom,
                Heading5MarginBottom = options.Heading5MarginBottom,
                HeadingColor = options.HeadingColor,
                Heading2Color = options.Heading2Color,
                Heading3Color = options.Heading3Color,
                Heading4Color = options.Heading4Color,
                Heading5Color = options.Heading5Color,
                BodyColor = options.BodyColor,
                ShowPageNumbers = options.ShowPageNumbers,
                HeaderText = options.HeaderText,
                FooterText = options.FooterText,
                HeaderFontSize = options.HeaderFontSize,
                FooterFontSize = options.FooterFontSize,
                HeaderFontColor = options.HeaderFontColor,
                FooterFontColor = options.FooterFontColor,
                HeaderAlignment = options.HeaderAlignment,
                FooterAlignment = options.FooterAlignment,
                RunningContentStartAt = options.RunningContentStartAt,
                TableBorderColor = options.TableBorderColor,
                TableHeaderBackground = options.TableHeaderBackground,
                TableHeaderFontColor = options.TableHeaderFontColor,
                CodeBorderColor = options.CodeBorderColor,
                HeaderHeight = options.HeaderHeight,
                FooterHeight = options.FooterHeight,
                FooterImagePath = options.FooterImagePath,
                FooterImageWidth = options.FooterImageWidth,
                HeadingScale = options.HeadingScale,
                ParagraphSpacingAfter = options.ParagraphSpacingAfter,
                ParagraphSpacingBefore = options.ParagraphSpacingBefore,
                SectionSpacing = options.SectionSpacing,
                TitleMarginTop = options.TitleMarginTop,
                TitleMarginBottom = options.TitleMarginBottom,
                CodeBackground = options.CodeBackground,
                CodespanBackground = options.CodespanBackground,
                LinkColor = options.LinkColor,
                TitleAlignment = options.TitleAlignment,
                BaseDirectory = inputDir,
            };
        }
        catch (Exception ex) when (ex is IOException or FormatException)
        {
            Console.Error.WriteLine($"Warning: could not load PDF theme: {ex.Message}");
            return PdfRenderOptions.Default;
        }
    }

    private static bool ContainsDiagramBlocks(DocumentNode document)
    {
        foreach (var node in document.Children)
        {
            if (HasDiagramBlock(node))
                return true;
        }
        return false;

        static bool HasDiagramBlock(AstNode node)
        {
            if (node is DelimitedBlockNode { BlockKind: DelimitedBlockKind.Source } block
                && block.Language is "plantuml" or "mermaid" or "ditaa" or "graphviz" or "dot")
                return true;
            foreach (var child in node.Children)
            {
                if (HasDiagramBlock(child))
                    return true;
            }
            return false;
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
        bool isText = format is OutputFormat.Html or OutputFormat.DocBook or OutputFormat.Man or OutputFormat.Revealjs;
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
