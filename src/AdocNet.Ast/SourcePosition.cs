namespace AdocNet;

/// <summary>
/// A position in source text. Line and column are 1-based.
/// </summary>
public readonly record struct SourcePosition(int Line, int Column) : IComparable<SourcePosition>
{
    public static readonly SourcePosition None = new(0, 0);

    public bool IsNone => Line == 0 && Column == 0;

    public int CompareTo(SourcePosition other)
    {
        var lineCmp = Line.CompareTo(other.Line);
        return lineCmp != 0 ? lineCmp : Column.CompareTo(other.Column);
    }

    public static bool operator <(SourcePosition left, SourcePosition right) => left.CompareTo(right) < 0;
    public static bool operator >(SourcePosition left, SourcePosition right) => left.CompareTo(right) > 0;
    public static bool operator <=(SourcePosition left, SourcePosition right) => left.CompareTo(right) <= 0;
    public static bool operator >=(SourcePosition left, SourcePosition right) => left.CompareTo(right) >= 0;

    public override string ToString() => IsNone ? "(none)" : $"{Line}:{Column}";
}
