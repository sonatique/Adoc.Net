namespace AdocNet.LanguageServer;

internal static class CompletionResolver
{
    public static IReadOnlyList<string> Resolve(DocumentManager documents, string uri, int line, int col)
    {
        var text = documents.GetText(uri);
        if (text is null) return [];
        var lines = text.Split('\n');
        if (line < 0 || line >= lines.Length) return [];
        var lineText = lines[line];
        var prefix = lineText[..Math.Min(col + 1, lineText.Length)];

        // Inside <<...  -> suggest anchor IDs
        if (prefix.Contains("<<") && !prefix.Contains(">>"))
            return documents.GetAnchors(uri).ToList();

        // Inside {...  -> suggest attribute names
        int lastOpen = prefix.LastIndexOf('{');
        int lastClose = prefix.LastIndexOf('}');
        if (lastOpen >= 0 && lastOpen > lastClose)
            return documents.GetAttributes(uri).Keys.ToList();

        return [];
    }
}
