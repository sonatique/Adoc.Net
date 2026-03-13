using AdocNet;

namespace AdocNet.Parser;

/// <summary>
/// The output of an include-expansion pass: the fully-expanded source text
/// and any diagnostics produced during expansion.
/// </summary>
internal sealed record ExpandResult(string Text, IReadOnlyList<Diagnostic> Diagnostics);
