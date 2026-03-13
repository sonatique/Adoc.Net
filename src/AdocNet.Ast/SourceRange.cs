namespace AdocNet;

/// <summary>
/// A range in source text, defined by a start and end position (both inclusive).
/// </summary>
public readonly record struct SourceRange(SourcePosition Start, SourcePosition End)
{
    public static readonly SourceRange None = new(SourcePosition.None, SourcePosition.None);

    public bool IsNone => Start.IsNone && End.IsNone;

    public bool Contains(SourcePosition position) =>
        !IsNone && position >= Start && position <= End;

    public override string ToString() => IsNone ? "(none)" : $"{Start}-{End}";
}
