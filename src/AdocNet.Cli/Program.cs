using AdocNet;
using AdocNet.Converters.Html;

namespace AdocNet.Cli;

internal static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsageError = 2;

    private static int Main(string[] args)
    {
        var parsed = ParseArguments(args);

        if (parsed is CliArgs.ShowHelp)
        {
            PrintHelp(Console.Out);
            return ExitSuccess;
        }

        if (parsed is CliArgs.Error error)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine();
            PrintHelp(Console.Error);
            return ExitUsageError;
        }

        if (parsed is CliArgs.Preview previewArgs)
        {
            var previewLogger = new ConsoleLogger(Console.Out, Console.Error, verbose: true, quiet: false);
            var cmd = new PreviewCommand(previewLogger);
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            return cmd.Run(previewArgs, cts.Token);
        }

        var run = (CliArgs.Run)parsed;
        return Execute(run);
    }

    private static int Execute(CliArgs.Run run)
    {
        // Load project config
        var configDir = Directory.Exists(run.InputPath)
            ? Path.GetFullPath(run.InputPath)
            : Path.GetDirectoryName(Path.GetFullPath(run.InputPath)) ?? ".";

        var config = run.ConfigPath is not null
            ? ConfigLoader.LoadFrom(run.ConfigPath)
            : ConfigLoader.Discover(configDir);

        run = ConfigMerger.Merge(run, config);

        // Resolve relative outDir against the config/input directory
        if (run.OutDir is not null && !Path.IsPathRooted(run.OutDir))
            run = run with { OutDir = Path.Combine(configDir, run.OutDir) };

        var logger = new ConsoleLogger(Console.Out, Console.Error, run.Verbose, run.Quiet);
        var command = new ConvertCommand(logger);

        if (run.Watch)
        {
            var watch = new WatchCommand(command, logger);
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            return watch.Run(run, cts.Token);
        }

        if (Directory.Exists(run.InputPath))
            return command.ConvertDirectory(run);

        return command.ConvertFile(run.InputPath, run.OutputPath, run);
    }

    internal static string FormatDiagnostic(Diagnostic diag) => ConvertCommand.FormatDiagnostic(diag);

    // ── Argument parsing ─────────────────────────────────────────────────

    internal static CliArgs ParseArguments(string[] args)
    {
        if (args.Length > 0 && args[0] == "preview")
            return ParsePreviewArguments(args);

        string? inputPath = null;
        string? outputPath = null;
        string? outDir = null;
        bool dumpAst = false;
        bool styled = false;
        OutputFormat format = OutputFormat.Html;
        HtmlTheme theme = HtmlTheme.Default;
        bool watch = false;
        bool verbose = false;
        bool quiet = false;
        bool recursive = false;
        string? configPath = null;

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "--help" or "-h" or "-?")
                return new CliArgs.ShowHelp();

            if (arg is "--dump-ast")
            {
                dumpAst = true;
                continue;
            }

            if (arg is "--styled")
            {
                styled = true;
                continue;
            }

            if (arg is "--theme")
            {
                if (i + 1 >= args.Length)
                    return new CliArgs.Error("Option --theme requires a theme name (default, asciidoctor, clean).");
                styled = true;
                var themeStr = args[++i].ToLowerInvariant();
                theme = themeStr switch
                {
                    "default" => HtmlTheme.Default,
                    "asciidoctor" => HtmlTheme.Asciidoctor,
                    "clean" => HtmlTheme.Clean,
                    _ => HtmlTheme.None,
                };
                if (theme == HtmlTheme.None)
                    return new CliArgs.Error($"Unknown theme: {themeStr}. Available themes: default, asciidoctor, clean.");
                continue;
            }

            if (arg is "-o")
            {
                if (i + 1 >= args.Length)
                    return new CliArgs.Error("Option -o requires a file path argument.");
                outputPath = args[++i];
                continue;
            }

            if (arg is "-f" or "--format")
            {
                if (i + 1 >= args.Length)
                    return new CliArgs.Error("Option -f requires a format argument (html, pdf, docbook, epub).");
                var formatStr = args[++i].ToLowerInvariant();
                format = formatStr switch
                {
                    "html" => OutputFormat.Html,
                    "pdf" => OutputFormat.Pdf,
                    "docbook" or "xml" => OutputFormat.DocBook,
                    "epub" => OutputFormat.Epub,
                    _ => OutputFormat.Html,
                };
                if (formatStr is not "html" and not "pdf" and not "docbook" and not "xml" and not "epub")
                    return new CliArgs.Error($"Unknown format: {formatStr}. Supported formats: html, pdf, docbook, epub.");
                continue;
            }

            if (arg is "--out-dir")
            {
                if (i + 1 >= args.Length)
                    return new CliArgs.Error("Option --out-dir requires a directory path argument.");
                outDir = args[++i];
                continue;
            }

            if (arg is "--watch" or "-w")
            {
                watch = true;
                continue;
            }

            if (arg is "--verbose" or "-v")
            {
                verbose = true;
                continue;
            }

            if (arg is "--quiet" or "-q")
            {
                quiet = true;
                continue;
            }

            if (arg is "--recursive" or "-r")
            {
                recursive = true;
                continue;
            }

            if (arg is "--config")
            {
                if (i + 1 >= args.Length)
                    return new CliArgs.Error("Option --config requires a file path.");
                configPath = args[++i];
                continue;
            }

            if (arg.StartsWith('-'))
                return new CliArgs.Error($"Unknown option: {arg}");

            if (inputPath is not null)
                return new CliArgs.Error("Only one input file may be specified.");

            inputPath = arg;
        }

        if (inputPath is null)
            return new CliArgs.Error("No input file specified.");

        if (outputPath is not null && Directory.Exists(inputPath))
            return new CliArgs.Error("Option -o cannot be used with directory input. Use --out-dir instead.");

        if (verbose && quiet)
            return new CliArgs.Error("Options --verbose and --quiet cannot be used together.");

        return new CliArgs.Run(inputPath, outputPath, outDir, dumpAst, format, styled, theme, watch, verbose, quiet, recursive, configPath);
    }

    private static CliArgs ParsePreviewArguments(string[] args)
    {
        string? inputPath = null;
        int port = 5500;
        bool noOpen = false;
        bool recursive = false;
        HtmlTheme theme = HtmlTheme.Asciidoctor;

        for (int i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "--help" or "-h" or "-?")
                return new CliArgs.ShowHelp();

            if (arg is "--port")
            {
                if (i + 1 >= args.Length || !int.TryParse(args[i + 1], out port) || port < 1 || port > 65535)
                    return new CliArgs.Error("Option --port requires a valid port number (1-65535).");
                i++;
                continue;
            }

            if (arg is "--no-open") { noOpen = true; continue; }
            if (arg is "--recursive" or "-r") { recursive = true; continue; }

            if (arg is "--theme")
            {
                if (i + 1 >= args.Length)
                    return new CliArgs.Error("Option --theme requires a theme name.");
                theme = args[++i].ToLowerInvariant() switch
                {
                    "default" => HtmlTheme.Default,
                    "asciidoctor" => HtmlTheme.Asciidoctor,
                    "clean" => HtmlTheme.Clean,
                    _ => HtmlTheme.None,
                };
                if (theme == HtmlTheme.None)
                    return new CliArgs.Error("Unknown theme. Available: default, asciidoctor, clean.");
                continue;
            }

            if (arg.StartsWith('-'))
                return new CliArgs.Error($"Unknown preview option: {arg}");

            if (inputPath is not null)
                return new CliArgs.Error("Only one input path may be specified for preview.");

            inputPath = arg;
        }

        if (inputPath is null)
            return new CliArgs.Error("The preview command requires an input file or directory.");

        return new CliArgs.Preview(inputPath, port, noOpen, Styled: true, theme, recursive);
    }

    internal static void PrintHelp(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  adocnet <input.adoc|directory> [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  -o <file>          Write output to file");
        writer.WriteLine("  -f <format>        Output format: html (default), pdf, docbook, epub");
        writer.WriteLine("  --styled           Wrap HTML output in a full document with CSS theme");
        writer.WriteLine("  --theme <name>     Select CSS theme: default, asciidoctor, clean");
        writer.WriteLine("  --out-dir <dir>    Write output files to directory");
        writer.WriteLine("  --dump-ast         Print AST instead of rendering");
        writer.WriteLine("  -w, --watch        Watch input file for changes and re-render");
        writer.WriteLine("  -v, --verbose      Enable verbose output");
        writer.WriteLine("  -q, --quiet        Suppress non-error output");
        writer.WriteLine("  -r, --recursive    Process input directories recursively");
        writer.WriteLine("  --config <file>    Load project configuration (default: discover adocnet.json)");
        writer.WriteLine("  --help             Show help");
        writer.WriteLine();
        writer.WriteLine("Examples:");
        writer.WriteLine("  adocnet README.adoc                       Convert single file to stdout");
        writer.WriteLine("  adocnet docs/ -r -f html --out-dir build/ Convert directory to build/");
        writer.WriteLine("  adocnet docs/ --watch -v                  Watch and rebuild on changes");
        writer.WriteLine();
        writer.WriteLine("Preview:");
        writer.WriteLine("  adocnet preview <path> [--port N] [--no-open] [--theme name] [-r]");
    }
}

internal enum OutputFormat
{
    Html,
    Pdf,
    DocBook,
    Epub,
}

/// <summary>
/// Discriminated result of CLI argument parsing.
/// </summary>
internal abstract record CliArgs
{
    internal sealed record Run(
        string InputPath,
        string? OutputPath,
        string? OutDir,
        bool DumpAst,
        OutputFormat Format = OutputFormat.Html,
        bool Styled = false,
        HtmlTheme Theme = HtmlTheme.Default,
        bool Watch = false,
        bool Verbose = false,
        bool Quiet = false,
        bool Recursive = false,
        string? ConfigPath = null,
        IReadOnlyDictionary<string, string>? Attributes = null) : CliArgs;
    internal sealed record ShowHelp() : CliArgs;
    internal sealed record Preview(
        string InputPath,
        int Port = 5500,
        bool NoOpen = false,
        bool Styled = true,
        HtmlTheme Theme = HtmlTheme.Asciidoctor,
        bool Recursive = false) : CliArgs;
    internal sealed record Error(string Message) : CliArgs;
}
