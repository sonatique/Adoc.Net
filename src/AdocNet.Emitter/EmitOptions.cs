namespace AdocNet.Emitter;

/// <summary>
/// Options for <see cref="AsciidocEmitter"/>. Controls surface-form choices when
/// the emitter has to synthesise syntax (no source-anchored fallback available),
/// and enables the source-anchored fast path for round-trip byte preservation.
/// </summary>
public sealed class EmitOptions
{
    /// <summary>
    /// Marker character used for synthesised unordered lists. Per the AsciiDoc
    /// spec, both <c>*</c> and <c>-</c> are valid at the first level; <c>*</c>
    /// is the default because it nests (<c>**</c>, <c>***</c>, …).
    /// </summary>
    public char UnorderedListMarker { get; init; } = '*';

    /// <summary>
    /// When the emitter has access to the original source text and an AST node
    /// has a non-empty <c>Source</c> range, copy that source slice verbatim
    /// instead of re-synthesising syntax from the AST. This is what lets
    /// unchanged subtrees round-trip byte-identical.
    /// </summary>
    public bool PreserveOriginalWhenAvailable { get; init; }

    /// <summary>
    /// Original source text. Required when
    /// <see cref="PreserveOriginalWhenAvailable"/> is true. Used as the source
    /// to slice from when an AST node carries a valid <c>SourceRange</c>.
    /// </summary>
    public string? OriginalSource { get; init; }

    /// <summary>The default options: no source-anchoring, <c>*</c> ulist marker.</summary>
    public static EmitOptions Default { get; } = new();
}
