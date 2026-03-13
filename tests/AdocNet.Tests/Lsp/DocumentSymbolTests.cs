using AdocNet.LanguageServer;
using AdocNet.Parser;

namespace AdocNet.Tests.Lsp;

[TestFixture]
public class DocumentSymbolTests
{
    [Test]
    public void Extracts_flat_sections()
    {
        var result = AdocParser.Parse("= Doc\n\n== Section One\n\nText.\n\n== Section Two\n\nMore.\n");
        var symbols = SymbolExtractor.Extract(result.Document);

        Assert.That(symbols, Has.Count.EqualTo(2));
        Assert.That(symbols[0].Name, Is.EqualTo("Section One"));
        Assert.That(symbols[1].Name, Is.EqualTo("Section Two"));
    }

    [Test]
    public void Extracts_nested_sections()
    {
        var result = AdocParser.Parse("= Doc\n\n== Chapter\n\n=== Sub-Section\n\nText.\n");
        var symbols = SymbolExtractor.Extract(result.Document);

        Assert.That(symbols, Has.Count.EqualTo(1));
        Assert.That(symbols[0].Name, Is.EqualTo("Chapter"));
        Assert.That(symbols[0].Children, Has.Count.EqualTo(1));
        Assert.That(symbols[0].Children[0].Name, Is.EqualTo("Sub-Section"));
    }

    [Test]
    public void Empty_document_returns_empty_list()
    {
        var result = AdocParser.Parse("");
        var symbols = SymbolExtractor.Extract(result.Document);
        Assert.That(symbols, Is.Empty);
    }
}
