using AdocNet.Ast;

namespace AdocNet.LanguageServer;

internal sealed record SymbolInfo(
    string Name,
    int Level,
    SourceRange Source,
    IReadOnlyList<SymbolInfo> Children);

internal static class SymbolExtractor
{
    public static IReadOnlyList<SymbolInfo> Extract(DocumentNode document)
    {
        var sections = document.Children.OfType<SectionNode>().ToList();
        if (sections.Count == 0) return [];

        return BuildHierarchy(sections, 0, sections.Count);
    }

    private static IReadOnlyList<SymbolInfo> BuildHierarchy(
        List<SectionNode> sections, int start, int end)
    {
        if (start >= end) return [];

        var result = new List<SymbolInfo>();
        var topLevel = sections[start].Level;

        var i = start;
        while (i < end)
        {
            var section = sections[i];

            // Find the range of children: everything after this section
            // until the next section at the same or lower level.
            var childStart = i + 1;
            var childEnd = childStart;
            while (childEnd < end && sections[childEnd].Level > topLevel)
            {
                childEnd++;
            }

            var children = BuildHierarchy(sections, childStart, childEnd);
            result.Add(new SymbolInfo(section.Title, section.Level, section.Source, children));
            i = childEnd;
        }

        return result;
    }
}
