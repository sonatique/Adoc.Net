using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LspRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace AdocNet.LanguageServer;

internal sealed class DefinitionHandler(DocumentManager documents) : DefinitionHandlerBase
{
    protected override DefinitionRegistrationOptions CreateRegistrationOptions(
        DefinitionCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("asciidoc"),
        };

    public override Task<LocationOrLocationLinks?> Handle(DefinitionParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
        var line = (int)request.Position.Line;
        var col = (int)request.Position.Character;

        var location = DefinitionResolver.Resolve(documents, uri, line, col);
        if (location is null)
            return Task.FromResult<LocationOrLocationLinks?>(null);

        var pos = new Position(location.Value.Line, location.Value.Column);
        var result = new LocationOrLocationLinks(new Location
        {
            Uri = request.TextDocument.Uri,
            Range = new LspRange(pos, pos),
        });

        return Task.FromResult<LocationOrLocationLinks?>(result);
    }
}
