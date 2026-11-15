using System.Globalization;
using System.Text;
using AdocNet.Ast;

namespace AdocNet.Importers;

/// <summary>Severity of an import observation (same scale as the Docx importer's).</summary>
public enum ImportIssueSeverity
{
    /// <summary>Mapped to a close approximation.</summary>
    Info,

    /// <summary>Content preserved, presentation lost.</summary>
    Warning,

    /// <summary>Content dropped entirely.</summary>
    Loss,
}

/// <summary>One observation made while importing.</summary>
public sealed class ImportIssue
{
    public required ImportIssueSeverity Severity { get; init; }

    /// <summary>Stable machine-readable code, e.g. <c>html.script.dropped</c>.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable description.</summary>
    public required string Message { get; init; }

    /// <summary>1-based page (PDF) when known.</summary>
    public int? Page { get; init; }

    public override string ToString()
        => Page is int p
            ? $"{Severity}: {Code} (page {p.ToString(CultureInfo.InvariantCulture)}) — {Message}"
            : $"{Severity}: {Code} — {Message}";
}

/// <summary>
/// What the importer saw and what it represented. <see cref="Fidelity"/> is the share of
/// content units (blocks, inline spans, cells, images, links) that reached the AsciiDoc as an
/// equivalent construct, as in <c>DocxImportReport</c>.
/// </summary>
public sealed class ImportReport
{
    private readonly List<ImportIssue> _issues = [];

    public int Headings { get; internal set; }
    public int Paragraphs { get; internal set; }
    public int ListItems { get; internal set; }
    public int Tables { get; internal set; }
    public int TableCells { get; internal set; }
    public int CodeBlocks { get; internal set; }
    public int Images { get; internal set; }
    public int Links { get; internal set; }
    public int Footnotes { get; internal set; }
    /// <summary>PDF only: pages read.</summary>
    public int Pages { get; internal set; }

    public int TotalUnits { get; internal set; }
    public int MappedUnits { get; internal set; }

    /// <summary><see cref="MappedUnits"/> / <see cref="TotalUnits"/>, or 1.0 for an empty document.</summary>
    public double Fidelity => TotalUnits == 0 ? 1.0 : (double)MappedUnits / TotalUnits;

    public IReadOnlyList<ImportIssue> Issues => _issues;

    internal void Mapped() { TotalUnits++; MappedUnits++; }

    internal void Add(ImportIssueSeverity severity, string code, string message, int? page = null)
        => _issues.Add(new ImportIssue { Severity = severity, Code = code, Message = message, Page = page });

    /// <summary>A unit that could not be represented: counts against fidelity and files a loss.</summary>
    internal void Lost(string code, string message, int? page = null)
    {
        TotalUnits++;
        Add(ImportIssueSeverity.Loss, code, message, page);
    }

    /// <summary>A unit mapped approximately: counts as mapped, files a warning.</summary>
    internal void Approximated(string code, string message, int? page = null)
    {
        Mapped();
        Add(ImportIssueSeverity.Warning, code, message, page);
    }

    /// <summary>A short human-readable summary, one line per issue code.</summary>
    public string ToSummary()
    {
        var sb = new StringBuilder();
        sb.Append("fidelity: ").Append((Fidelity * 100).ToString("0.00", CultureInfo.InvariantCulture))
          .Append("% (").Append(MappedUnits).Append('/').Append(TotalUnits).Append(" units)\n");
        sb.Append("headings: ").Append(Headings).Append(", paragraphs: ").Append(Paragraphs)
          .Append(", list items: ").Append(ListItems).Append(", tables: ").Append(Tables)
          .Append(", code blocks: ").Append(CodeBlocks).Append(", images: ").Append(Images)
          .Append(", links: ").Append(Links).Append(", footnotes: ").Append(Footnotes);
        if (Pages > 0) sb.Append(", pages: ").Append(Pages);
        sb.Append('\n');
        if (_issues.Count == 0) return sb.ToString();

        sb.Append("issues:\n");
        foreach (var group in _issues.GroupBy(i => i.Code, StringComparer.Ordinal))
        {
            var first = group.First();
            sb.Append("  [").Append(first.Severity).Append("] ").Append(group.Key)
              .Append(" ×").Append(group.Count()).Append(" — ").Append(first.Message).Append('\n');
        }
        return sb.ToString();
    }
}

/// <summary>An image extracted from the source (a data URI, or a PDF's embedded image).</summary>
public sealed class ImportMediaItem
{
    /// <summary>Where it came from: <c>data-uri</c>, or <c>page 3 image 1</c> for a PDF.</summary>
    public required string Source { get; init; }

    /// <summary>Path used in the emitted <c>image::</c> macro, relative to the output document, e.g. <c>media/image1.png</c>.</summary>
    public required string RelativePath { get; init; }

    public required byte[] Content { get; init; }

    /// <summary>Absolute path the bytes were written to, when the importer wrote them.</summary>
    public string? WrittenPath { get; internal set; }
}

/// <summary>Outcome of an import.</summary>
public sealed class ImportResult
{
    /// <summary>The AsciiDoc source.</summary>
    public required string AsciiDoc { get; init; }

    /// <summary><see cref="AsciiDoc"/> parsed into an Adoc.Net AST.</summary>
    public required DocumentNode Document { get; init; }

    /// <summary>What was mapped, approximated and lost.</summary>
    public required ImportReport Report { get; init; }

    /// <summary>Extracted images referenced by <see cref="AsciiDoc"/>, in first-use order.</summary>
    public required IReadOnlyList<ImportMediaItem> Media { get; init; }
}

/// <summary>Options shared by the Markdown, HTML and PDF importers.</summary>
public sealed class ImportOptions
{
    public static ImportOptions Default { get; } = new();

    /// <summary>Directory extracted images are written to, relative to the output document; also the prefix of their <c>image::</c> targets.</summary>
    public string MediaDirectoryName { get; init; } = "media";

    /// <summary>
    /// Make a heading that opens the document the document title (<c>= Title</c>) when it is the
    /// only one of its level, shifting the other headings up a level: a lone <c>h1</c> / <c>#</c>
    /// (HTML, Markdown), or the lone largest heading (PDF). Applies only when no title comes from
    /// metadata (front matter <c>title</c>, PDF information; an HTML <c>&lt;title&gt;</c> is used only when no heading is promoted).
    /// </summary>
    public bool PromoteFirstHeadingToTitle { get; init; } = true;

    /// <summary>When false, <see cref="DocumentImporter.ConvertFile"/> leaves media in memory.</summary>
    public bool ExtractMedia { get; init; } = true;
}

/// <summary>Raised when the input cannot be read at all (not a PDF, unreadable stream). Content that merely cannot be represented is reported instead.</summary>
public sealed class ImportException : Exception
{
    public ImportException(string message) : base(message) { }
    public ImportException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Common entry points of the importers.</summary>
public abstract class DocumentImporter
{
    private protected DocumentImporter(ImportOptions? options) => Options = options ?? ImportOptions.Default;

    public ImportOptions Options { get; }

    /// <summary>Imports from a stream (PDF needs a seekable one; it is buffered otherwise).</summary>
    public abstract ImportResult Import(Stream input);

    /// <summary>Imports a file. Media stays in memory; nothing is written.</summary>
    public ImportResult ImportFile(string path)
    {
        if (path is null) throw new ArgumentNullException(nameof(path));
        using var stream = File.OpenRead(path);
        return Import(stream);
    }

    /// <summary>
    /// Converts <paramref name="inputPath"/> to AsciiDoc at <paramref name="adocPath"/> (UTF-8, no BOM),
    /// writing extracted images into the media directory beside it unless <see cref="ImportOptions.ExtractMedia"/> is off.
    /// </summary>
    public ImportResult ConvertFile(string inputPath, string adocPath)
    {
        if (adocPath is null) throw new ArgumentNullException(nameof(adocPath));
        var result = ImportFile(inputPath);
        var directory = Path.GetDirectoryName(Path.GetFullPath(adocPath)) ?? ".";
        Directory.CreateDirectory(directory);
        File.WriteAllText(adocPath, result.AsciiDoc, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        if (Options.ExtractMedia) WriteMedia(result.Media, directory);
        return result;
    }

    /// <summary>Writes media into <paramref name="baseDirectory"/>; refuses paths that would leave it.</summary>
    public static void WriteMedia(IReadOnlyList<ImportMediaItem> media, string baseDirectory)
    {
        if (media is null) throw new ArgumentNullException(nameof(media));
        var root = Path.GetFullPath(baseDirectory);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? root : root + Path.DirectorySeparatorChar;
        foreach (var item in media)
        {
            var target = Path.GetFullPath(Path.Combine(root, item.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                throw new ImportException($"Media path '{item.RelativePath}' leaves the output directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, item.Content);
            item.WrittenPath = target;
        }
    }

    /// <summary>Wraps converted AsciiDoc in a result (parsing it for <see cref="ImportResult.Document"/>).</summary>
    private protected static ImportResult Result(string asciiDoc, ImportReport report, IReadOnlyList<ImportMediaItem> media) => new()
    {
        AsciiDoc = asciiDoc,
        Document = AdocNet.Parser.AdocParser.Parse(asciiDoc).Document,
        Report = report,
        Media = media,
    };
}
