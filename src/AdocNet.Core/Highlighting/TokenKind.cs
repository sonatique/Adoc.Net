namespace AdocNet.Highlighting;

/// <summary>
/// Categories for syntax-highlighted tokens.
/// </summary>
public enum TokenKind
{
    /// <summary>Unclassified text (identifiers, whitespace).</summary>
    Plain,

    /// <summary>Language keywords (if, class, return, def, etc.).</summary>
    Keyword,

    /// <summary>String literals ("...", '...', etc.).</summary>
    String,

    /// <summary>Single-line and multi-line comments.</summary>
    Comment,

    /// <summary>Numeric literals (42, 3.14, 0xFF, etc.).</summary>
    Number,

    /// <summary>Type names (int, String, List, etc.).</summary>
    Type,

    /// <summary>Operators and punctuation ({, }, =, +, etc.).</summary>
    Punctuation,

    /// <summary>Annotations and attributes ([Obsolete], @Override, etc.).</summary>
    Attribute,

    /// <summary>Preprocessor directives (#include, #if, etc.).</summary>
    Preprocessor,
}
