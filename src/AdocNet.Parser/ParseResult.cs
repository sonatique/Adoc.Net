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
}
