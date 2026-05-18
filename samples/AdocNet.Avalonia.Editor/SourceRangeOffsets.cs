using AdocNet;

namespace AdocNet.Avalonia.Editor;

/// <summary>
/// Converts an AST <see cref="SourceRange"/> (1-based line + column) into
/// 0-based character offsets into a source string. This is the bridge
/// between the AST's positional coordinates and AvaloniaEdit's offset
/// model — needed by the Block-WYSIWYG controller to slice the original
/// source for in-place editing and to splice the result back.
/// </summary>
internal static class SourceRangeOffsets
{
    /// <summary>
    /// Returns the (start, length) pair in <paramref name="source"/>
    /// that corresponds to <paramref name="range"/>. The range's End
    /// position is treated as inclusive (matching <see cref="SourceRange"/>),
    /// so the returned length covers the character at End. Returns (0, 0)
    /// when the range is None or out of bounds.
    /// </summary>
    public static (int Start, int Length) Resolve(string source, SourceRange range)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (range.IsNone) return (0, 0);

        int start = -1;
        int end = -1;
        int line = 1;
        int col = 1;

        for (int i = 0; i <= source.Length; i++)
        {
            if (start < 0 && line == range.Start.Line && col == range.Start.Column)
                start = i;
            if (line == range.End.Line && col == range.End.Column)
            {
                end = i;
                break;
            }

            if (i >= source.Length) break;
            if (source[i] == '\n')
            {
                line++;
                col = 1;
            }
            else
            {
                col++;
            }
        }

        if (start < 0) return (0, 0);
        if (end < 0) end = source.Length - 1;

        // Inclusive end → length = end - start + 1. Clamp so a trailing
        // out-of-range end never overruns the source.
        int length = end - start + 1;
        if (start + length > source.Length) length = source.Length - start;
        if (length < 0) length = 0;
        return (start, length);
    }
}
