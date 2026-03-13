namespace AdocNet.LanguageServer;

internal readonly record struct DefinitionLocation(int Line, int Column);

internal static class DefinitionResolver
{
    public static DefinitionLocation? Resolve(DocumentManager documents, string uri, int line, int col)
    {
        var text = documents.GetText(uri);
        if (text is null) return null;
        var lines = text.Split('\n');
        if (line < 0 || line >= lines.Length) return null;

        var xrefId = HoverResolver.FindXrefAtPosition(lines[line], col);
        if (xrefId is null) return null;

        var result = documents.Get(uri);
        if (result is null) return null;

        var node = FindNodeById(result.Document, xrefId);
        if (node is null || node.Source.IsNone) return null;

        return new DefinitionLocation(node.Source.Start.Line - 1, node.Source.Start.Column - 1);
    }

    private static AdocNet.Ast.AstNode? FindNodeById(AdocNet.Ast.AstNode node, string id)
    {
        if (node is AdocNet.Ast.BlockNode block && block.Id == id)
            return node;
        foreach (var child in node.Children)
        {
            var found = FindNodeById(child, id);
            if (found is not null) return found;
        }
        return null;
    }
}
