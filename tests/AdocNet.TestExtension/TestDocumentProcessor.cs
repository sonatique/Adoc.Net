using AdocNet;
using AdocNet.Ast;
using AdocNet.Extensions;

namespace AdocNet.TestExtension;

/// <summary>
/// Test document processor that sets a document attribute to mark execution.
/// </summary>
public sealed class TestDocumentProcessor : IDocumentProcessor
{
    /// <inheritdoc />
    public bool Process(DocumentNode document, RenderContext context)
    {
        document.SetAttribute("test-extension-loaded", "true");
        return false;
    }
}
