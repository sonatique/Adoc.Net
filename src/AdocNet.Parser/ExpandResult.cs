using AdocNet;

namespace AdocNet.Parser;

/// <summary>
/// The output of an include-expansion pass: the fully-expanded source text,
/// any diagnostics produced during expansion, and a per-line provenance table
/// (<see cref="Origins"/>[i] describes expanded line <c>i + 1</c>).
/// </summary>
internal sealed record ExpandResult(string Text, IReadOnlyList<Diagnostic> Diagnostics, IReadOnlyList<LineOrigin> Origins);
