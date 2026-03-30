using AdocNet;
using AdocNet.Converters.Html;

namespace AdocNet.Cli;

/// <summary>
/// Main entry point for the adocnet CLI. Also serves as the shared entry point
/// for specialized tools (adocnet-pdf, adocnet-epub, adocnet-docbook).
/// </summary>
public static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsageError = 2;

    private static int Main(string[] args)
        => Run(args, OutputFormat.Html, "adocnet");

    /// <summary>
    /// Shared entry point for adocnet and specialized tools (adocnet-pdf, etc.).
    /// </summary>
    public static int Run(string[] args, OutputFormat defaultFormat, string toolName)
    {
        var parsed = ParseArguments(args, defaultFormat);

        if (parsed is CliArgs.ShowHelp)
        {
            PrintHelp(Console.Out, toolName, defaultFormat);
            return ExitSuccess;
        }

        if (parsed is CliArgs.Error error)
        {
            Console.Error.WriteLine(error.Message);
            Console.Error.WriteLine();
            PrintHelp(Console.Error, toolName, defaultFormat);
            return ExitUsageError;
        }

        if (parsed is CliArgs.Ext extArgs)
        {
            var extCmd = new ExtensionCommands();
            return extCmd.Execute(extArgs);
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

    internal static CliArgs ParseArguments(string[] args, OutputFormat defaultFormat = OutputFormat.Html)
    {
        if (args.Length > 0 && args[0] == "preview")
            return ParsePreviewArguments(args);

        if (args.Length > 0 && args[0] == "ext")
            return ExtensionCommands.ParseExtArguments(args);

        string? inputPath = null;
        string? outputPath = null;
        string? outDir = null;
        bool dumpAst = false;
        bool styled = false;
        bool outputToStdout = false;
        OutputFormat format = defaultFormat;
        HtmlTheme theme = HtmlTheme.Default;
        bool watch = false;
        bool verbose = false;
        bool quiet = false;
        bool recursive = false;
        string? configPath = null;
        Dictionary<string, string>? attributes = null;
        List<string>? extensionPaths = null;
        List<string>? extensionDirs = null;
        bool noAutoExtensions = false;

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

            if (arg is "-e" or "--embedded")
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
                    return new CliArgs.Error("Option -o requires a file path argument (use '-' for stdout).");
                var val = args[++i];
                if (val == "-")
                    outputToStdout = true;
                else
                    outputPath = val;
                continue;
            }

            if (arg is "-b" or "--backend")
            {
                if (i + 1 >= args.Length)
                    return new CliArgs.Error("Option -b requires a format argument (html5, html, pdf, docbook5, docbook, epub).");
                var formatStr = args[++i].ToLowerInvariant();
                format = formatStr switch
                {
                    "html" or "html5" => OutputFormat.Html,
                    "pdf" => OutputFormat.Pdf,
                    "docbook" or "docbook5" or "xml" => OutputFormat.DocBook,
                    "epub" => OutputFormat.Epub,
                    _ => OutputFormat.Html,
                };
                if (formatStr is not "html" and not "html5" and not "pdf" and not "docbook" and not "docbook5" and not "xml" and not "epub")
                    return new CliArgs.Error($"Unknown format: {formatStr}. Supported formats: html, html5, pdf, docbook, docbook5, epub.");
                continue;
            }

            if (arg is "-D" or "--destination-dir")
            {
                if (i + 1 >= args.Length)
                    return new CliArgs.Error("Option -D requires a directory path argument.");
                outDir = args[++i];
                continue;
            }

            if (arg is "-a" or "--attribute")
            {
                if (i + 1 >= args.Length)
                    return new CliArgs.Error("Option -a requires an attribute in the form 'name=value'.");
                var attrStr = args[++i];
                attributes ??= [];
                var eqIdx = attrStr.IndexOf('=');
                if (eqIdx > 0)
                    attributes[attrStr[..eqIdx]] = attrStr[(eqIdx + 1)..];
                else
                    attributes[attrStr] = "";
                continue;
            }

            if (arg is "-n" or "--section-numbers")
            {
                attributes ??= [];
                attributes["sectnums"] = "";
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

            if (arg is "--extensions")
            {
                if (i + 1 >= args.Length)
                    return new CliArgs.Error("Option --extensions requires a DLL path.");
                extensionPaths ??= new List<string>();
                extensionPaths.Add(args[++i]);
                continue;
            }

            if (arg is "--extension-dir")
            {
                if (i + 1 >= args.Length)
                    return new CliArgs.Error("Option --extension-dir requires a directory path.");
                extensionDirs ??= new List<string>();
                extensionDirs.Add(args[++i]);
                continue;
            }

            if (arg is "--no-auto-extensions")
            {
                noAutoExtensions = true;
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
            return new CliArgs.Error("Option -o cannot be used with directory input. Use -D instead.");

        if (verbose && quiet)
            return new CliArgs.Error("Options --verbose and --quiet cannot be used together.");

        // Default behavior (matching Asciidoctor): output to file, same name with format extension.
        // Use -o - to write to stdout instead.
        if (outputPath is null && !outputToStdout && !dumpAst && !Directory.Exists(inputPath))
            outputPath = Path.ChangeExtension(inputPath, FormatExtension(format));

        return new CliArgs.Run(inputPath, outputPath, outDir, dumpAst, format, styled, theme, watch, verbose, quiet, recursive, configPath,
            attributes is { Count: > 0 } ? attributes : null,
            extensionPaths is { Count: > 0 } ? extensionPaths : null,
            extensionDirs is { Count: > 0 } ? extensionDirs : null,
            noAutoExtensions);
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

    private static string FormatExtension(OutputFormat format) => format switch
    {
        OutputFormat.Html => ".html",
        OutputFormat.Pdf => ".pdf",
        OutputFormat.DocBook => ".xml",
        OutputFormat.Epub => ".epub",
        _ => ".html",
    };

    internal static void PrintHelp(TextWriter writer, string toolName = "adocnet", OutputFormat defaultFormat = OutputFormat.Html)
    {
        var ext = FormatExtension(defaultFormat);
        var fmtName = defaultFormat.ToString().ToLowerInvariant();

        writer.WriteLine("Usage:");
        writer.WriteLine($"  {toolName} <input.adoc|directory> [options]");
        writer.WriteLine();
        writer.WriteLine($"By default, output is written to a file with the same name and {ext}");
        writer.WriteLine("extension. Use -o - for stdout.");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine($"  -b, --backend <fmt>   Output format: html5, pdf, docbook5, epub (default: {fmtName})");
        writer.WriteLine("  -o <file>             Write output to file (use '-' for stdout)");
        writer.WriteLine("  -D, --destination-dir <dir>  Write output files to directory");
        writer.WriteLine("  -a, --attribute <k=v> Set a document attribute");
        writer.WriteLine("  -n, --section-numbers Auto-number section titles");
        writer.WriteLine("  -e, --embedded        Wrap HTML in a full document with CSS theme");
        writer.WriteLine("  --theme <name>        Select CSS theme: default, asciidoctor, clean");
        writer.WriteLine("  --dump-ast            Print AST instead of rendering");
        writer.WriteLine("  -w, --watch           Watch input file for changes and re-render");
        writer.WriteLine("  -v, --verbose         Enable verbose output");
        writer.WriteLine("  -q, --quiet           Suppress non-error output");
        writer.WriteLine("  -r, --recursive       Process input directories recursively");
        writer.WriteLine("  --config <file>       Load project configuration (default: discover adocnet.json)");
        writer.WriteLine("  --extensions <path>   Load extensions from a DLL file (repeatable)");
        writer.WriteLine("  --extension-dir <dir> Load all extension DLLs from directory (repeatable)");
        writer.WriteLine("  --no-auto-extensions  Skip loading installed extensions from ~/.adocnet/extensions/");
        writer.WriteLine("  -h, --help            Show help");
        writer.WriteLine();
        writer.WriteLine("Examples:");
        writer.WriteLine($"  {toolName} README.adoc                       Convert to README{ext}");
        writer.WriteLine($"  {toolName} README.adoc -o -                  Convert to stdout");
        writer.WriteLine($"  {toolName} README.adoc -o custom{ext}        Convert to custom{ext}");
        writer.WriteLine($"  {toolName} docs/ -r -D build/                Convert directory to build/");
        writer.WriteLine($"  {toolName} docs/ --watch -v                  Watch and rebuild on changes");
        writer.WriteLine($"  {toolName} README.adoc -a version=2.0        Set document attribute");

        if (toolName == "adocnet")
        {
            writer.WriteLine();
            writer.WriteLine("Preview:");
            writer.WriteLine("  adocnet preview <path> [--port N] [--no-open] [--theme name] [-r]");
            writer.WriteLine();
            writer.WriteLine("Extension management:");
            writer.WriteLine("  adocnet ext list              List installed extensions");
            writer.WriteLine("  adocnet ext install <path>    Install extension from directory [--force]");
            writer.WriteLine("  adocnet ext remove <name>     Remove an installed extension");
        }
    }
}

public enum OutputFormat
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
        IReadOnlyDictionary<string, string>? Attributes = null,
        IReadOnlyList<string>? ExtensionPaths = null,
        IReadOnlyList<string>? ExtensionDirs = null,
        bool NoAutoExtensions = false) : CliArgs;
    internal sealed record ShowHelp() : CliArgs;
    internal sealed record Preview(
        string InputPath,
        int Port = 5500,
        bool NoOpen = false,
        bool Styled = true,
        HtmlTheme Theme = HtmlTheme.Asciidoctor,
        bool Recursive = false) : CliArgs;
    internal sealed record Error(string Message) : CliArgs;

    internal abstract record Ext() : CliArgs
    {
        internal sealed record ExtList() : Ext;
        internal sealed record ExtInstall(string SourcePath, bool Force = false) : Ext;
        internal sealed record ExtRemove(string Name) : Ext;
        internal sealed record ExtInfo(string Name) : Ext;
        internal sealed record ExtSearch(string Keyword) : Ext;
    }
}
