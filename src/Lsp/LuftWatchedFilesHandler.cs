using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Workspace;

namespace Luft.Lsp;

public class LuftWatchedFilesHandler(LuftCompilerService compiler, DiagnosticPublisher publisher) : DidChangeWatchedFilesHandlerBase
{
    public override Task<Unit> Handle(DidChangeWatchedFilesParams request, CancellationToken cancellationToken)
    {
        compiler.ApplyDiskChanges(request.Changes.Select(c => (LuftCompilerService.PathOf(c.Uri), c.Type)));
        publisher.PublishAll();
        return Unit.Task;
    }

    protected override DidChangeWatchedFilesRegistrationOptions CreateRegistrationOptions(DidChangeWatchedFilesCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            Watchers = new Container<OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher>(
                new OmniSharp.Extensions.LanguageServer.Protocol.Models.FileSystemWatcher { GlobPattern = "**/*.aero" })
        };
}
