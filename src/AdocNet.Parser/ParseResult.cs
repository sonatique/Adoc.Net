using AdocNet;
using AdocNet.Ast;

namespace AdocNet.Parser;

/// <summary>
/// The output of a parse operation: the document AST and any diagnostics produced.
/// </summary>
public sealed record ParseResult(DocumentNode Document, IReadOnlyList<Diagnostic> Diagnostics)
{
    /// <summary>Returns true when any diagnostic has <see cref="DiagnosticSeverity.Error"/> severity.</summary>
    public bool HasErrors => Diagnostics.Any(d => d.IsError);

    /// <summary>Returns true when any diagnostic has <see cref="DiagnosticSeverity.Warning"/> severity.</summary>
    public bool HasWarnings => Diagnostics.Any(d => d.IsWarning);

    /// <summary>
    /// Per-line source provenance for the fully-expanded document: entry <c>i</c>
    /// describes expanded line <c>i + 1</c> (the coordinate space AST
    /// <see cref="SourcePosition"/>s count in). Maps an AST line back to the file and
    /// line the author edits — across <c>include::</c> expansion (incl. <c>tags=</c>/
    /// <c>lines=</c>/<c>leveloffset=</c> filtering and nesting), conditional directives,
    /// and front-matter stripping.
    /// <para>
    /// Empty when no source was available (e.g. constructed directly). Populated by
    /// <see cref="AdocParser.Parse(string, ParseOptions)"/>.
    /// </para>
    /// </summary>
    public IReadOnlyList<LineOrigin> LineOrigins { get; init; } = [];

    /// <summary>
    /// Looks up the <see cref="LineOrigin"/> for a 1-based <paramref name="expandedLine"/>
    /// (an AST <see cref="SourcePosition.Line"/>). Returns <c>false</c> with
    /// <see cref="LineOrigin.None"/> when the line is out of range or no provenance is
    /// available.
    /// </summary>
    public bool TryGetLineOrigin(int expandedLine, out LineOrigin origin)
    {
        if (expandedLine >= 1 && expandedLine <= LineOrigins.Count)
        {
            origin = LineOrigins[expandedLine - 1];
            return true;
        }
        origin = LineOrigin.None;
        return false;
    }

    /// <summary>
    /// Translates a 1-based expanded (AST <see cref="SourcePosition.Line"/>) line to the
    /// original-source line the author edits, via <see cref="LineOrigins"/>. Returns the
    /// input line unchanged when no provenance is available or the source line is unknown.
    /// <para>
    /// Note: <see cref="Diagnostic.Range"/>s are already reported in source coordinates,
    /// so this helper is for mapping <em>AST node</em> positions (e.g. for click-to-source
    /// over the rendered document); diagnostics need no further translation.
    /// </para>
    /// </summary>
    public int ToSourceLine(int expandedLine)
    {
        if (expandedLine >= 1 && expandedLine <= LineOrigins.Count)
        {
            var line = LineOrigins[expandedLine - 1].SourceLine;
            if (line > 0)
                return line;
        }
        return expandedLine;
    }
}
