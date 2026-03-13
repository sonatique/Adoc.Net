namespace AdocNet;

/// <summary>
/// Flags describing which substitutions to apply when processing inline text.
/// Aligned with the AsciiDoc specification's six-phase substitution model.
/// </summary>
[Flags]
public enum SubstitutionKind
{
    /// <summary>No substitutions — raw content with no escaping.</summary>
    None = 0,

    /// <summary>Phase 2: Inline formatting — <c>*bold*</c>, <c>_italic_</c>, <c>`mono`</c>.</summary>
    Quotes = 1,

    /// <summary>Phase 5: Inline macros — <c>link:[]</c>, <c>image:[]</c>, bare URLs.</summary>
    Macros = 2,

    /// <summary>Phase 3: Attribute references — <c>{name}</c> expansion.</summary>
    Attributes = 4,

    /// <summary>Phase 6: Post-replacements — smart punctuation, hard line breaks.</summary>
    PostReplacements = 8,

    /// <summary>Phase 1: Special characters — HTML-escape <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>.</summary>
    SpecialCharacters = 16,

    /// <summary>Phase 4: Replacements — <c>(C)</c> → ©, <c>-></c> → →, character entities.</summary>
    Replacements = 32,

    /// <summary>Backward-compatible alias for <see cref="Quotes"/>.</summary>
    InlineFormatting = Quotes,

    /// <summary>All substitutions enabled — the default for normal text contexts.</summary>
    Normal = SpecialCharacters | Quotes | Attributes | Replacements | Macros | PostReplacements,

    /// <summary>Verbatim contexts — only special character escaping.</summary>
    Verbatim = SpecialCharacters,

    /// <summary>Header/title contexts — all phases except post-replacements.</summary>
    Header = SpecialCharacters | Quotes | Attributes | Replacements | Macros,
}
