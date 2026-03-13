using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace AdocNet.LanguageServer;

internal sealed class DocumentSymbolHandler(DocumentManager documents)
    : DocumentSymbolHandlerBase
{
    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(
        DocumentSymbolCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("asciidoc"),
        };

    public override Task<SymbolInformationOrDocumentSymbolContainer?> Handle(
        DocumentSymbolParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
        var result = documents.Get(uri);
        if (result is null)
            return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
                new SymbolInformationOrDocumentSymbolContainer());

        var symbols = SymbolExtractor.Extract(result.Document);
        var lspSymbols = symbols.Select(MapSymbol).ToArray();

        return Task.FromResult<SymbolInformationOrDocumentSymbolContainer?>(
            new SymbolInformationOrDocumentSymbolContainer(
                lspSymbols.Select(s => new SymbolInformationOrDocumentSymbol(s))));
    }

    private static DocumentSymbol MapSymbol(SymbolInfo info) => new()
    {
        Name = info.Name,
        Kind = SymbolKind.Module,
        Range = MapRange(info.Source),
        SelectionRange = MapRange(info.Source),
        Children = new Container<DocumentSymbol>(info.Children.Select(MapSymbol)),
    };

    private static LspRange MapRange(SourceRange source)
    {
        if (source.IsNone)
            return new(new(0, 0), new(0, 0));
        return new(
            new(source.Start.Line - 1, source.Start.Column - 1),
            new(source.End.Line - 1, source.End.Column - 1));
    }
}
