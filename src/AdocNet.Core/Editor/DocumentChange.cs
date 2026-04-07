using System.Text;

namespace AdocNet.Editor;

/// <summary>
/// An immutable text change: replace <see cref="Length"/> characters starting at
/// <see cref="Offset"/> with <see cref="NewText"/>.
/// </summary>
public readonly struct DocumentChange
{
    /// <summary>Zero-based character offset into the document text.</summary>
    public int Offset { get; }

    /// <summary>Number of characters to remove (0 for pure insertion).</summary>
    public int Length { get; }

    /// <summary>Replacement text (empty string for pure deletion).</summary>
    public string NewText { get; }

    /// <summary>
    /// Creates a new document change.
    /// </summary>
    /// <param name="offset">Zero-based character offset.</param>
    /// <param name="length">Number of characters to remove.</param>
    /// <param name="newText">Replacement text.</param>
    public DocumentChange(int offset, int length, string newText)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Offset = offset;
        Length = length;
        NewText = newText ?? throw new ArgumentNullException(nameof(newText));
    }

    /// <summary>
    /// Applies a sequence of changes to a text string. Changes are applied sequentially;
    /// each change's offset refers to the text state after all preceding changes.
    /// </summary>
    /// <param name="text">The original text.</param>
    /// <param name="changes">The changes to apply in order.</param>
    /// <returns>The resulting text after all changes.</returns>
    public static string ApplyAll(string text, IReadOnlyList<DocumentChange> changes)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        if (changes is null) throw new ArgumentNullException(nameof(changes));

        if (changes.Count == 0)
            return text;

        var sb = new StringBuilder(text);
        foreach (var change in changes)
        {
            if (change.Offset > sb.Length)
                throw new ArgumentOutOfRangeException(nameof(changes),
                    $"Change offset {change.Offset} exceeds text length {sb.Length}.");
            if (change.Offset + change.Length > sb.Length)
                throw new ArgumentOutOfRangeException(nameof(changes),
                    $"Change at offset {change.Offset} with length {change.Length} exceeds text length {sb.Length}.");

            sb.Remove(change.Offset, change.Length);
            sb.Insert(change.Offset, change.NewText);
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public override string ToString() => $"@{Offset} -{Length} +\"{NewText}\"";
}
