using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace AdocNet.LanguageServer;

internal sealed class CompletionHandler(DocumentManager documents) : CompletionHandlerBase
{
    protected override CompletionRegistrationOptions CreateRegistrationOptions(
        CompletionCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("asciidoc"),
            TriggerCharacters = new Container<string>("<", "{"),
        };

    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
        var line = (int)request.Position.Line;
        var col = (int)request.Position.Character;

        var suggestions = CompletionResolver.Resolve(documents, uri, line, col);

        // Determine kind based on context: if we're inside <<, these are references; inside {, variables
        var text = documents.GetText(uri);
        var kind = CompletionItemKind.Reference;
        if (text is not null)
        {
            var lines = text.Split('\n');
            if (line >= 0 && line < lines.Length)
            {
                var prefix = lines[line][..Math.Min(col + 1, lines[line].Length)];
                int lastOpen = prefix.LastIndexOf('{');
                int lastClose = prefix.LastIndexOf('}');
                if (lastOpen >= 0 && lastOpen > lastClose)
                    kind = CompletionItemKind.Variable;
            }
        }

        var items = suggestions.Select(s => new CompletionItem
        {
            Label = s,
            Kind = kind,
        }).ToArray();

        return Task.FromResult(new CompletionList(items));
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken ct)
        => Task.FromResult(request);
}
