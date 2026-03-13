using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.LanguageServer;

internal sealed class DocumentManager
{
    private readonly Dictionary<string, ParseResult> _documents = [];
    private readonly Dictionary<string, string> _sourceTexts = [];

    public ParseResult Parse(string uri, string text)
    {
        var options = new ParseOptions { SourceFilePath = uri };
        var result = AdocParser.Parse(text, options);
        _documents[uri] = result;
        _sourceTexts[uri] = text;
        return result;
    }

    public ParseResult? Get(string uri) =>
        _documents.TryGetValue(uri, out var result) ? result : null;

    public string? GetText(string uri) =>
        _sourceTexts.TryGetValue(uri, out var text) ? text : null;

    public void Remove(string uri)
    {
        _documents.Remove(uri);
        _sourceTexts.Remove(uri);
    }

    public IReadOnlyList<string> GetAnchors(string uri)
    {
        if (!_documents.TryGetValue(uri, out var result)) return [];
        var ids = new List<string>();
        CollectIds(result.Document, ids);
        return ids;
    }

    public IReadOnlyDictionary<string, string> GetAttributes(string uri)
    {
        if (!_documents.TryGetValue(uri, out var result)) return new Dictionary<string, string>();
        return result.Document.Attributes;
    }

    private static void CollectIds(AstNode node, List<string> ids)
    {
        if (node is BlockNode block && block.Id is not null)
            ids.Add(block.Id);
        foreach (var child in node.Children)
            CollectIds(child, ids);
    }
}
