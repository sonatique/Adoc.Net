using System.Text;
using AdocNet.Ast;

namespace AdocNet;

/// <summary>
/// Convenience extension methods for <see cref="IDocumentRenderer"/>.
/// </summary>
public static class DocumentRendererExtensions
{
    /// <summary>
    /// Renders the document to a UTF-8 string.
    /// </summary>
    /// <param name="renderer">The renderer to use.</param>
    /// <param name="doc">The parsed document AST.</param>
    /// <param name="options">Optional render options. Uses <see cref="RenderOptions.Default"/> when null.</param>
    /// <returns>The rendered output as a string.</returns>
    public static string RenderToString(this IDocumentRenderer renderer, DocumentNode doc, RenderOptions? options = null)
    {
        using var ms = new MemoryStream();
        renderer.Render(doc, ms, options ?? RenderOptions.Default);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>
    /// Renders the document to a UTF-8 byte array.
    /// </summary>
    /// <param name="renderer">The renderer to use.</param>
    /// <param name="doc">The parsed document AST.</param>
    /// <param name="options">Optional render options. Uses <see cref="RenderOptions.Default"/> when null.</param>
    /// <returns>The rendered output as a byte array.</returns>
    public static byte[] RenderToBytes(this IDocumentRenderer renderer, DocumentNode doc, RenderOptions? options = null)
    {
        using var ms = new MemoryStream();
        renderer.Render(doc, ms, options ?? RenderOptions.Default);
        return ms.ToArray();
    }
}
