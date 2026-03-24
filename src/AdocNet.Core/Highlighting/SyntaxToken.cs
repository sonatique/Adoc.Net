namespace AdocNet.Highlighting;

/// <summary>
/// A single highlighted token: a run of text with a classification.
/// Tokens are contiguous — concatenating all token texts reproduces the original source.
/// </summary>
public readonly record struct SyntaxToken(TokenKind Kind, string Text);
