using System.Text;
using AdocNet.Ast;

namespace AdocNet;

public static class DocumentRendererExtensions
{
    public static string RenderToString(this IDocumentRenderer renderer, DocumentNode doc, RenderOptions? options = null)
    {
        using var ms = new MemoryStream();
        renderer.Render(doc, ms, options ?? RenderOptions.Default);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    public static byte[] RenderToBytes(this IDocumentRenderer renderer, DocumentNode doc, RenderOptions? options = null)
    {
        using var ms = new MemoryStream();
        renderer.Render(doc, ms, options ?? RenderOptions.Default);
        return ms.ToArray();
    }
}
