namespace AdocNet.LanguageServer;

internal static class HoverResolver
{
    /// <summary>Returns hover markdown, or null. Line and col are 0-based.</summary>
    public static string? Resolve(DocumentManager documents, string uri, int line, int col)
    {
        var text = documents.GetText(uri);
        if (text is null) return null;
        var lines = text.Split('\n');
        if (line < 0 || line >= lines.Length) return null;
        var lineText = lines[line];
        if (col < 0 || col >= lineText.Length) return null;

        // Check for xref: <<id>> or <<id,label>>
        var xrefId = FindXrefAtPosition(lineText, col);
        if (xrefId is not null)
        {
            var title = FindSectionTitle(documents.Get(uri)?.Document, xrefId);
            return title is not null ? $"**Section:** {title}" : $"**Anchor:** `{xrefId}`";
        }

        // Check for attribute: {name}
        var attrName = FindAttributeAtPosition(lineText, col);
        if (attrName is not null)
        {
            var attrs = documents.GetAttributes(uri);
            return attrs.TryGetValue(attrName, out var value)
                ? $"**Attribute** `{attrName}`: {value}"
                : $"**Unresolved attribute:** `{attrName}`";
        }

        return null;
    }

    internal static string? FindXrefAtPosition(string line, int col)
    {
        // Search backwards from col for <<
        int start = -1;
        for (int i = Math.Min(col, line.Length - 1); i >= 1; i--)
        {
            if (line[i] == '<' && line[i - 1] == '<') { start = i - 1; break; }
        }
        if (start < 0) return null;
        int end = line.IndexOf(">>", start + 2, StringComparison.Ordinal);
        if (end < 0 || col > end + 1) return null;
        var content = line[(start + 2)..end];
        var comma = content.IndexOf(',');
        return comma >= 0 ? content[..comma].Trim() : content.Trim();
    }

    internal static string? FindAttributeAtPosition(string line, int col)
    {
        int start = -1;
        for (int i = Math.Min(col, line.Length - 1); i >= 0; i--)
        {
            if (line[i] == '{') { start = i; break; }
            if (line[i] == '}') break; // went past a closing brace
        }
        if (start < 0) return null;
        int end = line.IndexOf('}', start + 1);
        if (end < 0 || col > end) return null;
        return line[(start + 1)..end];
    }

    private static string? FindSectionTitle(AdocNet.Ast.AstNode? node, string id)
    {
        if (node is null) return null;
        if (node is AdocNet.Ast.BlockNode block && block.Id == id && node is AdocNet.Ast.SectionNode section)
            return section.Title;
        foreach (var child in node.Children)
        {
            var found = FindSectionTitle(child, id);
            if (found is not null) return found;
        }
        return null;
    }
}
