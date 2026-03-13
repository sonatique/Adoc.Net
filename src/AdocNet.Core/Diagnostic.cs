namespace AdocNet;

/// <summary>
/// A diagnostic message produced during parsing or include expansion.
/// </summary>
public sealed record Diagnostic(DiagnosticSeverity Severity, string Message, SourceRange Range)
{
    /// <summary>
    /// Optional file path for diagnostics originating from include expansion.
    /// Null for diagnostics from the main document.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>Returns true when this diagnostic has <see cref="DiagnosticSeverity.Error"/> severity.</summary>
    public bool IsError => Severity == DiagnosticSeverity.Error;

    /// <summary>Returns true when this diagnostic has <see cref="DiagnosticSeverity.Warning"/> severity.</summary>
    public bool IsWarning => Severity == DiagnosticSeverity.Warning;

    public override string ToString() =>
        FilePath is not null
            ? $"{Severity} at {FilePath} {Range}: {Message}"
            : $"{Severity} at {Range}: {Message}";
}
