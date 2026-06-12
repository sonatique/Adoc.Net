namespace AdocNet;

/// <summary>
/// Options controlling how AsciiDoc source text is parsed.
/// </summary>
public sealed class ParseOptions
{
    /// <summary>
    /// The file path of the source document. Used for include resolution
    /// and diagnostic messages. When null, includes are not resolved.
    /// </summary>
    public string? SourceFilePath { get; init; }

    /// <summary>
    /// Base directory for resolving relative <c>include::</c> paths.
    /// Defaults to the directory containing <see cref="SourceFilePath"/> when that is set,
    /// or the current working directory otherwise.
    /// </summary>
    public string? BaseDirectory { get; init; }

    /// <summary>
    /// Maximum nesting depth for recursive <c>include::</c> directives.
    /// Defaults to <c>10</c>.
    /// </summary>
    public int IncludeMaxDepth { get; init; } = 10;

    /// <summary>
    /// Whether to expand <c>include::</c> directives in the source text.
    /// Defaults to <c>true</c> when <see cref="BaseDirectory"/> or
    /// <see cref="SourceFilePath"/> is set, <c>false</c> otherwise.
    /// </summary>
    public bool? ExpandIncludes { get; init; }

    /// <summary>
    /// External attributes to pre-populate before parsing.
    /// These are available for conditional evaluation (<c>ifdef</c>/<c>ifndef</c>/<c>ifeval</c>)
    /// and inline substitution (<c>{name}</c>) from the start of the document.
    /// Document-defined attributes (header or body) with the same name will override these.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }

    /// <summary>
    /// Set of attribute names that cannot be overridden by the document.
    /// When a document header or body defines an attribute whose name is in this set,
    /// the assignment is silently ignored.
    /// </summary>
    public IReadOnlySet<string>? LockedAttributes { get; init; }

    /// <summary>
    /// Custom file reader for <c>include::</c> expansion.
    /// When null, the default filesystem reader is used.
    /// Provide a custom implementation to resolve includes from databases,
    /// HTTP sources, embedded resources, or virtual filesystems.
    /// </summary>
    public IIncludeReader? IncludeReader { get; init; }

    /// <summary>
    /// Whether to allow <c>include::</c> directives that reference remote URLs
    /// (<c>http://</c> or <c>https://</c>). Defaults to <c>false</c> for security.
    /// When false, URL includes are skipped and a warning diagnostic is emitted.
    /// </summary>
    public bool AllowUriRead { get; init; }

    /// <summary>
    /// The safe mode for document processing.
    /// Controls restrictions on includes, file I/O, and attribute overrides.
    /// <para>
    /// Default: <see cref="SafeMode.Safe"/>. This confines <c>include::</c> resolution to the
    /// document's base directory, blocking parent-directory (<c>..</c>), absolute, and UNC paths
    /// so that processing untrusted documents cannot disclose arbitrary local files. Legitimate
    /// in-tree includes continue to work. Set this to <see cref="SafeMode.Unsafe"/> only when the
    /// document source is trusted (e.g. a local CLI invocation on your own files).
    /// </para>
    /// </summary>
    public SafeMode SafeMode { get; init; } = SafeMode.Safe;

    /// <summary>A shared default instance with no options set.</summary>
    public static ParseOptions Default { get; } = new();

    /// <summary>
    /// Resolves the effective base directory for include expansion.
    /// </summary>
    internal string? ResolveBaseDirectory()
    {
        if (BaseDirectory is not null)
            return BaseDirectory;
        if (SourceFilePath is not null)
            return Path.GetDirectoryName(Path.GetFullPath(SourceFilePath));
        return null;
    }

    /// <summary>
    /// Resolves whether includes should be expanded.
    /// </summary>
    internal bool ShouldExpandIncludes()
    {
        if (ExpandIncludes.HasValue)
            return ExpandIncludes.Value;
        return ResolveBaseDirectory() is not null;
    }
}
