using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace AdocNet.Importers.Docx;

/// <summary>Severity of a single import observation.</summary>
public enum DocxIssueSeverity
{
    /// <summary>Mapped to a close approximation — e.g. a colour kept as a role.</summary>
    Info,

    /// <summary>Content preserved, presentation lost.</summary>
    Warning,

    /// <summary>Content dropped entirely.</summary>
    Loss,
}

/// <summary>One observation made while importing a .docx.</summary>
public sealed class DocxImportIssue
{
    public required DocxIssueSeverity Severity { get; init; }

    /// <summary>Stable machine-readable code, e.g. <c>textbox.dropped</c>.</summary>
    public required string Code { get; init; }

    /// <summary>Human-readable description.</summary>
    public required string Message { get; init; }

    /// <summary>1-based index of the body paragraph the issue was found in, when known.</summary>
    public int? ParagraphIndex { get; init; }

    public override string ToString()
        => ParagraphIndex is int p
            ? $"{Severity}: {Code} (paragraph {p.ToString(CultureInfo.InvariantCulture)}) — {Message}"
            : $"{Severity}: {Code} — {Message}";
}

/// <summary>
/// What the importer saw and what it managed to represent.
/// <para>
/// <see cref="Fidelity"/> is a <em>content-mapping</em> ratio: the share of
/// content-bearing WordprocessingML units (paragraphs, runs and their
/// formatting toggles, table cells, images, hyperlinks, notes, list items)
/// that reached the AST as an equivalent AsciiDoc construct. It deliberately
/// says nothing about visual fidelity: AsciiDoc has no model for page geometry,
/// fonts, colours or spacing, so a pixel-level comparison against Word is not
/// a meaningful target.
/// </para>
/// </summary>
public sealed class DocxImportReport
{
    private readonly List<DocxImportIssue> _issues = new();

    public int Paragraphs { get; internal set; }
    public int Runs { get; internal set; }
    public int Sections { get; internal set; }
    public int ListItems { get; internal set; }
    public int Tables { get; internal set; }
    public int TableCells { get; internal set; }
    public int Images { get; internal set; }
    public int Hyperlinks { get; internal set; }
    public int Footnotes { get; internal set; }
    public int Bookmarks { get; internal set; }

    /// <summary>Content units the importer encountered.</summary>
    public int TotalUnits { get; internal set; }

    /// <summary>Content units mapped to an AsciiDoc construct without loss.</summary>
    public int MappedUnits { get; internal set; }

    /// <summary>
    /// <see cref="MappedUnits"/> / <see cref="TotalUnits"/>, or 1.0 for an
    /// empty document.
    /// </summary>
    public double Fidelity => TotalUnits == 0 ? 1.0 : (double)MappedUnits / TotalUnits;

    public IReadOnlyList<DocxImportIssue> Issues => _issues;

    internal void Count(bool mapped)
    {
        TotalUnits++;
        if (mapped) MappedUnits++;
    }

    internal void Add(DocxIssueSeverity severity, string code, string message, int? paragraphIndex = null)
    {
        _issues.Add(new DocxImportIssue
        {
            Severity = severity,
            Code = code,
            Message = message,
            ParagraphIndex = paragraphIndex,
        });
    }

    /// <summary>
    /// Records a unit that could not be represented: counts it against
    /// fidelity and files an issue.
    /// </summary>
    internal void Lost(string code, string message, int? paragraphIndex = null)
    {
        Count(mapped: false);
        Add(DocxIssueSeverity.Loss, code, message, paragraphIndex);
    }

    /// <summary>
    /// Records a unit that was mapped approximately: it counts as mapped, but
    /// the approximation is filed so the caller can see it.
    /// </summary>
    internal void Approximated(string code, string message, int? paragraphIndex = null)
    {
        Count(mapped: true);
        Add(DocxIssueSeverity.Warning, code, message, paragraphIndex);
    }

    /// <summary>Renders a short human-readable summary, one issue per line.</summary>
    public string ToSummary()
    {
        var sb = new StringBuilder();
        sb.Append("fidelity: ")
          .Append((Fidelity * 100).ToString("0.00", CultureInfo.InvariantCulture))
          .Append("% (")
          .Append(MappedUnits.ToString(CultureInfo.InvariantCulture))
          .Append('/')
          .Append(TotalUnits.ToString(CultureInfo.InvariantCulture))
          .Append(" units)\n");
        sb.Append("paragraphs: ").Append(Paragraphs)
          .Append(", runs: ").Append(Runs)
          .Append(", sections: ").Append(Sections)
          .Append(", lists items: ").Append(ListItems)
          .Append(", tables: ").Append(Tables)
          .Append(", images: ").Append(Images)
          .Append(", links: ").Append(Hyperlinks)
          .Append(", footnotes: ").Append(Footnotes)
          .Append('\n');

        if (_issues.Count == 0) return sb.ToString();

        // Group identical codes so a document with 200 coloured runs reports
        // one line, not 200.
        var counts = new Dictionary<string, (DocxIssueSeverity Severity, string Message, int Count)>(StringComparer.Ordinal);
        foreach (var issue in _issues)
        {
            if (counts.TryGetValue(issue.Code, out var existing))
                counts[issue.Code] = (existing.Severity, existing.Message, existing.Count + 1);
            else
                counts[issue.Code] = (issue.Severity, issue.Message, 1);
        }

        sb.Append("issues:\n");
        foreach (var pair in counts)
        {
            sb.Append("  [").Append(pair.Value.Severity).Append("] ")
              .Append(pair.Key).Append(" ×").Append(pair.Value.Count)
              .Append(" — ").Append(pair.Value.Message).Append('\n');
        }

        return sb.ToString();
    }
}
