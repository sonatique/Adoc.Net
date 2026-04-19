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

        return sections.Select(BuildSymbol).ToList();
    }

    private static SymbolInfo BuildSymbol(SectionNode section)
    {
        var children = section.Children.OfType<SectionNode>()
            .Select(BuildSymbol).ToList();
        return new SymbolInfo(section.Title, section.Level, section.Source, children);
    }
}
