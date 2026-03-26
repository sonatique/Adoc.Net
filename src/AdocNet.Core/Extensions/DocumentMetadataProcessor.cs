using AdocNet.Ast;

namespace AdocNet.Extensions;

/// <summary>
/// Example document processor that inserts a metadata paragraph at the start of the document.
/// Demonstrates <see cref="IDocumentProcessor"/> with whole-tree modification.
/// </summary>
public sealed class DocumentMetadataProcessor : IDocumentProcessor
{
    private readonly string _text;

    /// <summary>
    /// Initializes the processor with the metadata text to insert.
    /// </summary>
    /// <param name="text">The text for the metadata paragraph.</param>
    public DocumentMetadataProcessor(string text)
    {
        _text = text ?? throw new ArgumentNullException(nameof(text));
    }

    /// <inheritdoc />
    public void Process(DocumentNode document)
    {
        var para = new ParagraphNode { Text = _text, Inlines = [new TextInlineNode { Value = _text }] };
        document.InsertChild(0, para);
    }
}
