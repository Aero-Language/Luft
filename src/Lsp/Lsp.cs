using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Server;

namespace Luft.Lsp;

public static class Lsp
{
    static async Task Main(string[] args)
    {
        var server = await LanguageServer.From(options => options
            .WithInput(Console.OpenStandardInput())
            .WithOutput(Console.OpenStandardOutput())
            .WithLoggerFactory(new LoggerFactory())
            .WithServices(services =>
            {
                services.AddSingleton<LuftCompilerService>();
            })
            .WithHandler<LuftTextDocumentSyncHandler>()
            .WithHandler<LuftCompletionHandler>()
            .WithHandler<LuftHoverHandler>());

        await server.WaitForExit;
    }
}