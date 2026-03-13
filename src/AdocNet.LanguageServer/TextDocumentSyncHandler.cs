using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using LspDiagnostic = OmniSharp.Extensions.LanguageServer.Protocol.Models.Diagnostic;

namespace AdocNet.LanguageServer;

internal sealed class TextDocumentSyncHandler(
    DocumentManager documents,
    ILanguageServerFacade server) : TextDocumentSyncHandlerBase
{
    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
        => new(uri, "asciidoc");

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(
        TextSynchronizationCapability capability,
        ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("asciidoc"),
            Change = TextDocumentSyncKind.Full,
        };

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
        var result = documents.Parse(uri, request.TextDocument.Text);
        PublishDiagnostics(request.TextDocument.Uri, result);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken ct)
    {
        var uri = request.TextDocument.Uri.ToString();
        var text = request.ContentChanges.First().Text!;
        var result = documents.Parse(uri, text);
        PublishDiagnostics(request.TextDocument.Uri, result);
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken ct)
    {
        documents.Remove(request.TextDocument.Uri.ToString());
        server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = request.TextDocument.Uri,
            Diagnostics = new Container<LspDiagnostic>(),
        });
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken ct)
        => Unit.Task;

    private void PublishDiagnostics(DocumentUri uri, AdocNet.Parser.ParseResult result)
    {
        var diagnostics = result.Diagnostics
            .Select(DiagnosticMapper.Map)
            .ToArray();

        server.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<LspDiagnostic>(diagnostics),
        });
    }
}
