using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;

namespace AdocNet.LanguageServer;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var server = await OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(options =>
        {
            options
                .WithInput(Console.OpenStandardInput())
                .WithOutput(Console.OpenStandardOutput())
                .ConfigureLogging(logging =>
                {
                    logging.SetMinimumLevel(LogLevel.Warning);
                })
                .WithServices(services =>
                {
                    services.AddSingleton<DocumentManager>();
                })
                .WithHandler<TextDocumentSyncHandler>()
                .WithHandler<DocumentSymbolHandler>()
                .WithHandler<HoverHandler>()
                .WithHandler<DefinitionHandler>()
                .WithHandler<CompletionHandler>();
        });

        await server.WaitForExit;
    }
}
