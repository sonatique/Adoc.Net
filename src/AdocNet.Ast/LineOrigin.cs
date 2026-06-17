namespace AdocNet;

/// <summary>
/// The origin of a single line of fully-expanded source text — the file and
/// line the author actually edits, before the parser folded in <c>include::</c>
/// directives and stripped conditional/front-matter content.
/// <para>
/// AST <see cref="SourcePosition"/>s count lines in the <em>expanded</em> text,
/// so a consumer that edits or syncs against the original editor buffer
/// (scroll-sync, click-to-source, in-place editing) needs to translate an AST
/// line back to the line the user sees. A document's line-origin table provides
/// exactly that mapping, produced authoritatively by the parser that performed
/// the expansion.
/// </para>
/// </summary>
/// <param name="SourceFile">
/// Absolute (or as-resolved) path of the file the line came from. For lines of
/// the primary document this is the path supplied via
/// <c>ParseOptions.SourceFilePath</c> (so it is <c>null</c> when parsing a
/// string with no associated file); for included content it is the resolved
/// include path (or the URL, for URL includes).
/// </param>
/// <param name="SourceLine">
/// 1-based line number within <see cref="SourceFile"/>. <c>0</c> when unknown.
/// </param>
/// <param name="IsSynthetic">
/// <c>true</c> when the line was pulled in from an included file rather than the
/// primary document, and therefore has no representation in the primary editor
/// buffer (it is not editable in place there — though <see cref="SourceFile"/>
/// /<see cref="SourceLine"/> still locate it in the include).
/// </param>
public readonly record struct LineOrigin(string? SourceFile, int SourceLine, bool IsSynthetic)
{
    /// <summary>An unknown origin: no file, line 0, not synthetic.</summary>
    public static readonly LineOrigin None = new(null, 0, false);

    /// <summary>True when this is the <see cref="None"/> sentinel (no known origin).</summary>
    public bool IsNone => SourceFile is null && SourceLine == 0 && !IsSynthetic;

    /// <inheritdoc />
    public override string ToString() =>
        IsNone ? "(none)" : $"{SourceFile ?? "<input>"}:{SourceLine}{(IsSynthetic ? " (include)" : "")}";
}
