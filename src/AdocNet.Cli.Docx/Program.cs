using System.Globalization;
using AdocNet.Importers.Docx;

namespace AdocNet.Cli.Docx;

/// <summary>
/// <c>docx2adoc</c>: converts Word documents to AsciiDoc, extracting images
/// alongside the output and reporting what could not be represented.
/// </summary>
public static class Program
{
    private const int ExitSuccess = 0;
    private const int ExitUsageError = 2;
    private const int ExitConversionError = 1;
    private const int ExitFidelityBelowThreshold = 3;

    public static int Main(string[] args)
    {
        var options = CommandLine.Parse(args, out var error);
        if (error is not null)
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine();
            PrintHelp(Console.Error);
            return ExitUsageError;
        }

        if (options!.ShowHelp)
        {
            PrintHelp(Console.Out);
            return ExitSuccess;
        }

        try
        {
            return Convert(options);
        }
        catch (DocxImportException ex)
        {
            Console.Error.WriteLine($"docx2adoc: {ex.Message}");
            return ExitConversionError;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"docx2adoc: {ex.Message}");
            return ExitConversionError;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"docx2adoc: {ex.Message}");
            return ExitConversionError;
        }
    }

    private static int Convert(CommandLine options)
    {
        var importer = new DocxImporter(options.ImportOptions);

        DocxImportResult result;
        if (options.OutputPath is null)
        {
            // Writing to stdout: media cannot be placed relative to an output
            // file, so it stays in memory and is only counted in the report.
            result = importer.ImportFile(options.InputPath!);
            Console.Out.Write(new Emitter.AsciidocEmitter().Emit(result.Document));
        }
        else
        {
            result = importer.ConvertFile(options.InputPath!, options.OutputPath);
            if (!options.Quiet)
            {
                Console.Error.WriteLine($"docx2adoc: wrote {options.OutputPath}"
                    + (result.Media.Count > 0 ? $" and {result.Media.Count} media file(s)" : string.Empty));
            }
        }

        if (options.ShowReport)
        {
            Console.Error.Write(result.Report.ToSummary());
        }

        if (options.MinimumFidelity is double minimum && result.Report.Fidelity < minimum)
        {
            Console.Error.WriteLine(
                $"docx2adoc: fidelity {(result.Report.Fidelity * 100).ToString("0.00", CultureInfo.InvariantCulture)}%"
                + $" is below the required {(minimum * 100).ToString("0.00", CultureInfo.InvariantCulture)}%");
            return ExitFidelityBelowThreshold;
        }

        return ExitSuccess;
    }

    private static void PrintHelp(TextWriter writer)
    {
        writer.WriteLine("docx2adoc — convert a Word document to AsciiDoc");
        writer.WriteLine();
        writer.WriteLine("Usage:");
        writer.WriteLine("  docx2adoc <input.docx> [-o <output.adoc>] [options]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  -o, --output <path>      Output file. Defaults to stdout (no media is written).");
        writer.WriteLine("      --media-dir <name>   Media directory name beside the output (default: media).");
        writer.WriteLine("      --no-media           Do not write extracted images.");
        writer.WriteLine("      --report             Print the fidelity report to stderr.");
        writer.WriteLine("      --min-fidelity <n>   Exit with code 3 when fidelity is below n (0-100).");
        writer.WriteLine("      --reject-revisions   Keep the original text of tracked changes, not the revised one.");
        writer.WriteLine("      --comments           Import Word comments as paragraphs with the comment role.");
        writer.WriteLine("      --no-admonitions     Do not detect NOTE:/TIP:/WARNING: paragraphs and callout boxes.");
        writer.WriteLine("      --no-code-blocks     Do not detect monospaced paragraphs as listing blocks.");
        writer.WriteLine("      --no-properties      Do not import core properties into the document header.");
        writer.WriteLine("      --plain-formatting   Drop underline/strikethrough/caps instead of keeping them as roles.");
        writer.WriteLine("      --keep-heading       Do not promote a leading top-level heading to the document title.");
        writer.WriteLine("  -q, --quiet              Suppress the progress line on stderr.");
        writer.WriteLine("  -h, --help               Show this help.");
        writer.WriteLine();
        writer.WriteLine("Exit codes: 0 success, 1 conversion error, 2 usage error, 3 fidelity below --min-fidelity.");
    }

    /// <summary>Parsed command line.</summary>
    private sealed class CommandLine
    {
        public string? InputPath { get; private set; }
        public string? OutputPath { get; private set; }
        public bool ShowHelp { get; private set; }
        public bool ShowReport { get; private set; }
        public bool Quiet { get; private set; }
        public double? MinimumFidelity { get; private set; }
        public DocxImportOptions ImportOptions { get; private set; } = DocxImportOptions.Default;

        public static CommandLine? Parse(string[] args, out string? error)
        {
            error = null;
            var result = new CommandLine();

            if (args.Length == 0)
            {
                result.ShowHelp = true;
                return result;
            }

            var mediaDirectory = "media";
            var extractMedia = true;
            var detectAdmonitions = true;
            var detectCodeBlocks = true;
            var importProperties = true;
            var formattingRoles = true;
            var promoteHeading = true;
            var trackedChanges = TrackedChangeHandling.Accept;
            var comments = CommentHandling.Ignore;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "-h":
                    case "--help":
                        result.ShowHelp = true;
                        return result;

                    case "-o":
                    case "--output":
                        if (++i >= args.Length) { error = $"docx2adoc: {arg} needs a path"; return null; }
                        result.OutputPath = args[i];
                        break;

                    case "--media-dir":
                        if (++i >= args.Length) { error = "docx2adoc: --media-dir needs a name"; return null; }
                        mediaDirectory = args[i];
                        break;

                    case "--no-media": extractMedia = false; break;
                    case "--report": result.ShowReport = true; break;
                    case "--reject-revisions": trackedChanges = TrackedChangeHandling.Reject; break;
                    case "--comments": comments = CommentHandling.LineComments; break;
                    case "--no-admonitions": detectAdmonitions = false; break;
                    case "--no-code-blocks": detectCodeBlocks = false; break;
                    case "--no-properties": importProperties = false; break;
                    case "--plain-formatting": formattingRoles = false; break;
                    case "--keep-heading": promoteHeading = false; break;
                    case "-q":
                    case "--quiet":
                        result.Quiet = true;
                        break;

                    case "--min-fidelity":
                        if (++i >= args.Length) { error = "docx2adoc: --min-fidelity needs a percentage"; return null; }
                        if (!double.TryParse(args[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
                            || percent < 0 || percent > 100)
                        {
                            error = $"docx2adoc: '{args[i]}' is not a percentage between 0 and 100";
                            return null;
                        }

                        result.MinimumFidelity = percent / 100.0;
                        break;

                    default:
                        if (arg.StartsWith("-", StringComparison.Ordinal))
                        {
                            error = $"docx2adoc: unknown option '{arg}'";
                            return null;
                        }

                        if (result.InputPath is not null)
                        {
                            error = "docx2adoc: more than one input file given";
                            return null;
                        }

                        result.InputPath = arg;
                        break;
                }
            }

            if (result.InputPath is null)
            {
                error = "docx2adoc: no input file given";
                return null;
            }

            if (!File.Exists(result.InputPath))
            {
                error = $"docx2adoc: input file not found: {result.InputPath}";
                return null;
            }

            result.ImportOptions = new DocxImportOptions
            {
                MediaDirectoryName = mediaDirectory,
                ExtractMedia = extractMedia,
                DetectAdmonitions = detectAdmonitions,
                DetectCodeBlocks = detectCodeBlocks,
                ImportCoreProperties = importProperties,
                PreserveFormattingAsRoles = formattingRoles,
                PromoteFirstHeadingToTitle = promoteHeading,
                TrackedChanges = trackedChanges,
                Comments = comments,
            };

            return result;
        }
    }
}
