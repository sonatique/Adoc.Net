namespace AdocNet;

/// <summary>
/// A diagnostic message produced during parsing or include expansion.
/// </summary>
/// <remarks>
/// <see cref="Range"/> is reported in <em>original-source</em> coordinates — the
/// file and line the author edits — not post-<c>include::</c>-expansion (AST)
/// coordinates. So a diagnostic that follows an include reports the source line
/// (matching asciidoctor), and <see cref="FilePath"/> names the included file
/// when the diagnostic originates inside one, making <c>file:line</c> directly
/// usable. (To translate an <em>AST node</em> position back to source, use
/// <c>ParseResult.ToSourceLine</c>.)
/// </remarks>
public sealed record Diagnostic(DiagnosticSeverity Severity, string Message, SourceRange Range)
{
    /// <summary>
    /// The file the diagnostic is located in: the resolved include path when the
    /// diagnostic originates inside an included file, otherwise the primary source
    /// path (or null when parsing a string with no associated file).
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>Returns true when this diagnostic has <see cref="DiagnosticSeverity.Error"/> severity.</summary>
    public bool IsError => Severity == DiagnosticSeverity.Error;

    /// <summary>Returns true when this diagnostic has <see cref="DiagnosticSeverity.Warning"/> severity.</summary>
    public bool IsWarning => Severity == DiagnosticSeverity.Warning;

    /// <summary>Returns a human-readable representation including severity, location, and message.</summary>
    public override string ToString() =>
        FilePath is not null
            ? $"{Severity} at {FilePath} {Range}: {Message}"
            : $"{Severity} at {Range}: {Message}";
}
