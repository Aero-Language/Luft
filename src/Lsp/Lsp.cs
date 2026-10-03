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
                services.AddSingleton<DiagnosticPublisher>();
            })
            .WithHandler<LuftTextDocumentSyncHandler>()
            .WithHandler<LuftCompletionHandler>()
            .WithHandler<LuftHoverHandler>()
            .WithHandler<LuftDocumentSymbolHandler>()
            .WithHandler<LuftDefinitionHandler>()
            .WithHandler<LuftSemanticTokensHandler>()
            .OnStarted((languageServer, _) =>
            {
                // Pull in every .aero file of the workspace so cross-file modules and imports resolve
                try
                {
                    var compiler = languageServer.Services.GetRequiredService<LuftCompilerService>();
                    var roots = new List<string>();

                    if (languageServer.ClientSettings.WorkspaceFolders is { } folders)
                        roots.AddRange(folders.Select(f => f.Uri.GetFileSystemPath()));
                    else if (languageServer.ClientSettings.RootUri is { } root)
                        roots.Add(root.GetFileSystemPath());

                    foreach (var r in roots) compiler.LoadWorkspace(r);
                    languageServer.Services.GetRequiredService<DiagnosticPublisher>().PublishAll();
                }
                catch (Exception) { }

                return Task.CompletedTask;
            }));

        await server.WaitForExit;
    }
}