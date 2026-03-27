using AdocNet.Ast;
using AdocNet.Extensions;

namespace AdocNet.TestExtension;

/// <summary>
/// Test document processor that sets a document attribute to mark execution.
/// </summary>
public sealed class TestDocumentProcessor : IDocumentProcessor
{
    /// <inheritdoc />
    public void Process(DocumentNode document)
    {
        document.SetAttribute("test-extension-loaded", "true");
    }
}
