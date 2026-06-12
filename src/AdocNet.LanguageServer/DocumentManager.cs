using System.Collections.Concurrent;
using AdocNet.Ast;
using AdocNet.Parser;

namespace AdocNet.LanguageServer;

internal sealed class DocumentManager
{
    // Keyed by the LSP document URI. ConcurrentDictionary because didChange (Parse) can run while
    // hover/completion/definition handlers read on other threads.
    private readonly ConcurrentDictionary<string, ParseResult> _documents = new();
    private readonly ConcurrentDictionary<string, string> _sourceTexts = new();

    public ParseResult Parse(string uri, string text)
    {
        // The LSP gives a document URI (e.g. "file:///c%3A/docs/book.adoc"). Pass the decoded
        // filesystem path — not the raw URI string — as SourceFilePath so include:: resolves
        // against the document's directory instead of a bogus "<cwd>/file:/..." path. For
        // non-file documents (e.g. untitled:), skip include resolution entirely.
        var filePath = ToFileSystemPath(uri);
        var options = filePath is not null
            ? new ParseOptions { SourceFilePath = filePath }
            : ParseOptions.Default;
        var result = AdocParser.Parse(text, options);
        _documents[uri] = result;
        _sourceTexts[uri] = text;
        return result;
    }

    private static string? ToFileSystemPath(string uri)
    {
        if (Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile)
            return parsed.LocalPath;
        return null;
    }

    public ParseResult? Get(string uri) =>
        _documents.TryGetValue(uri, out var result) ? result : null;

    public string? GetText(string uri) =>
        _sourceTexts.TryGetValue(uri, out var text) ? text : null;

    public void Remove(string uri)
    {
        _documents.TryRemove(uri, out _);
        _sourceTexts.TryRemove(uri, out _);
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
