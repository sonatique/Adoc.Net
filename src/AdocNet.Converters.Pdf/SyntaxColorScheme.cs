using AdocNet.Highlighting;

namespace AdocNet.Converters.Pdf;

/// <summary>
/// Maps syntax token categories to PDF colors for source block highlighting.
/// </summary>
public sealed class SyntaxColorScheme
{
    /// <summary>Color for language keywords. Default: dark blue.</summary>
    public PdfColor Keyword { get; init; }

    /// <summary>Color for string literals. Default: dark red.</summary>
    public PdfColor String { get; init; }

    /// <summary>Color for comments. Default: green.</summary>
    public PdfColor Comment { get; init; }

    /// <summary>Color for numeric literals. Default: dark cyan.</summary>
    public PdfColor Number { get; init; }

    /// <summary>Color for type names. Default: teal.</summary>
    public PdfColor Type { get; init; }

    /// <summary>Color for punctuation. Default: dark gray.</summary>
    public PdfColor Punctuation { get; init; }

    /// <summary>Color for attributes/annotations. Default: purple.</summary>
    public PdfColor Attribute { get; init; }

    /// <summary>Color for preprocessor directives. Default: gray.</summary>
    public PdfColor Preprocessor { get; init; }

    /// <summary>Default color scheme matching the design document.</summary>
    public static SyntaxColorScheme Default { get; } = new()
    {
        Keyword      = new(0f, 0f, 0.75f),
        String       = new(0.64f, 0.08f, 0.08f),
        Comment      = new(0f, 0.5f, 0f),
        Number       = new(0.04f, 0.53f, 0.34f),
        Type         = new(0.15f, 0.5f, 0.6f),
        Punctuation  = new(0.31f, 0.31f, 0.31f),
        Attribute    = new(0.55f, 0f, 0.55f),
        Preprocessor = new(0.5f, 0.5f, 0.5f),
    };

    /// <summary>Gets the color for a given token kind. Returns null for Plain (use default text color).</summary>
    public PdfColor? GetColor(TokenKind kind) => kind switch
    {
        TokenKind.Keyword => Keyword,
        TokenKind.String => String,
        TokenKind.Comment => Comment,
        TokenKind.Number => Number,
        TokenKind.Type => Type,
        TokenKind.Punctuation => Punctuation,
        TokenKind.Attribute => Attribute,
        TokenKind.Preprocessor => Preprocessor,
        _ => null,
    };
}
