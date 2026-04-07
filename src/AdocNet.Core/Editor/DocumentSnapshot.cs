using AdocNet.Ast;

namespace AdocNet.Editor;

/// <summary>
/// An immutable snapshot of a document at a specific version.
/// Holds the full text and an optional parsed document with diagnostics.
/// </summary>
public sealed class DocumentSnapshot
{
    /// <summary>Monotonically increasing version number (starts at 0 for initial).</summary>
    public int Version { get; }

    /// <summary>The full document text at this version.</summary>
    public string Text { get; }

    /// <summary>
    /// The parsed document for this version, or null if not yet parsed.
    /// Populated by the engine's ParseIncremental method.
    /// </summary>
    public DocumentNode? Document { get; }

    /// <summary>
    /// Diagnostics from parsing this version. Empty if not yet parsed.
    /// </summary>
    public IReadOnlyList<Diagnostic> Diagnostics { get; }

    /// <summary>
    /// Creates a snapshot with the given version, text, and optional parsed document.
    /// </summary>
    public DocumentSnapshot(int version, string text, DocumentNode? document = null, IReadOnlyList<Diagnostic>? diagnostics = null)
    {
        if (version < 0) throw new ArgumentOutOfRangeException(nameof(version));
        Version = version;
        Text = text ?? throw new ArgumentNullException(nameof(text));
        Document = document;
        Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
    }

    /// <summary>
    /// Creates an initial snapshot (version 0) from the given text.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <returns>A new snapshot at version 0 with no parse result.</returns>
    public static DocumentSnapshot Initial(string text)
    {
        return new DocumentSnapshot(0, text);
    }

    /// <summary>
    /// Applies a sequence of changes and returns a new snapshot with incremented version.
    /// The new snapshot has no parsed document; call the engine's ParseIncremental method
    /// to obtain one.
    /// </summary>
    /// <param name="changes">The text changes to apply.</param>
    /// <returns>A new snapshot with updated text and incremented version.</returns>
    public DocumentSnapshot ApplyChanges(IReadOnlyList<DocumentChange> changes)
    {
        if (changes is null) throw new ArgumentNullException(nameof(changes));

        var newText = DocumentChange.ApplyAll(Text, changes);
        return new DocumentSnapshot(Version + 1, newText);
    }
}
