namespace AdocNet.Emitter;

/// <summary>
/// Maps 1-based <c>(line, column)</c> <see cref="SourcePosition"/> coordinates
/// to 0-based character offsets into a source string. Built lazily on first
/// use so the source-anchored emit path pays the indexing cost only when
/// <see cref="EmitOptions.PreserveOriginalWhenAvailable"/> is enabled.
/// </summary>
internal sealed class SourceOffsetTable
{
    private readonly string _source;
    private readonly int[] _lineStarts;

    public SourceOffsetTable(string source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));

        // Build line-start offsets. Line N (1-based) starts at _lineStarts[N-1].
        // We treat both "\n" and "\r\n" by indexing the position immediately
        // after a '\n'. This is permissive: standalone "\r" line endings are
        // not produced by AdocNet's parser.
        var starts = new List<int> { 0 };
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\n')
                starts.Add(i + 1);
        }
        _lineStarts = starts.ToArray();
    }

    public string Source => _source;

    /// <summary>
    /// Returns the 0-based character offset corresponding to a 1-based
    /// <see cref="SourcePosition"/>, or -1 when the position is out of range
    /// (e.g. <see cref="SourcePosition.None"/>).
    /// </summary>
    public int OffsetOf(SourcePosition position)
    {
        if (position.IsNone) return -1;
        int lineIndex = position.Line - 1;
        if (lineIndex < 0 || lineIndex >= _lineStarts.Length) return -1;
        int lineStart = _lineStarts[lineIndex];

        // Column is 1-based; column 1 == start of line.
        int offset = lineStart + (position.Column - 1);
        return offset < 0 ? 0 : offset > _source.Length ? _source.Length : offset;
    }

    /// <summary>
    /// Slices the source text covered by <paramref name="range"/>. The end
    /// position is treated as inclusive (matching <see cref="SourceRange"/>'s
    /// contract), so the resulting substring includes the character at the
    /// <c>End</c> position. Returns null when the range is <c>None</c> or out
    /// of bounds for the source.
    /// </summary>
    public string? Slice(SourceRange range)
    {
        if (range.IsNone) return null;
        int start = OffsetOf(range.Start);
        int end = OffsetOf(range.End);
        if (start < 0 || end < 0 || end < start) return null;

        // Inclusive end → length = end - start + 1, clamped to source length.
        int length = Math.Min(_source.Length - start, end - start + 1);
        return _source.Substring(start, length);
    }
}
