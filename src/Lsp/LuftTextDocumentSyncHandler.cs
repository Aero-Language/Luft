using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;

namespace Luft.Lsp;

public class LuftTextDocumentSyncHandler(LuftCompilerService compiler, DiagnosticPublisher publisher) : TextDocumentSyncHandlerBase
{
    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri) => new(uri, "aero");

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        compiler.UpdateFile(request.TextDocument.Uri, request.TextDocument.Text);
        publisher.PublishAll();
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        // Full sync: the last change carries the whole text
        compiler.UpdateFile(request.TextDocument.Uri, request.ContentChanges.Last().Text);
        publisher.PublishAll();
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
    {
        compiler.CloseFile(request.TextDocument.Uri);
        publisher.PublishAll();
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken) => Unit.Task;

    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions(TextSynchronizationCapability syncCapability, ClientCapabilities capability)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("aero"),
            Change = TextDocumentSyncKind.Full
        };
}