using Luft.Utility;
using MediatR;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;
using OmniSharp.Extensions.LanguageServer.Protocol.Server.Capabilities;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Luft.Lsp;

public class LuftTextDocumentSyncHandler : TextDocumentSyncHandlerBase
{
    private readonly LuftCompilerService _compiler;
    private readonly ILanguageServerFacade _router;

    public LuftTextDocumentSyncHandler(LuftCompilerService compiler, ILanguageServerFacade router)
    {
        _compiler = compiler;
        _router = router;
    }

    public override TextDocumentAttributes GetTextDocumentAttributes(DocumentUri uri)
        => new(uri, "aero");

    public override Task<Unit> Handle(DidChangeTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri;
        var code = request.ContentChanges.First().Text;
        
        _compiler.UpdateFile(uri, code);
        PublishDiagnostics(uri);
        
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidOpenTextDocumentParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri;
        
        _compiler.UpdateFile(uri, request.TextDocument.Text);
        PublishDiagnostics(uri);
        
        return Unit.Task;
    }

    public override Task<Unit> Handle(DidCloseTextDocumentParams request, CancellationToken cancellationToken)
        => Unit.Task;

    public override Task<Unit> Handle(DidSaveTextDocumentParams request, CancellationToken cancellationToken)
        => Unit.Task;
    
    private void PublishDiagnostics(DocumentUri uri)
    {
        var errors = _compiler.GetDiagnostics(uri);

        var lspDiagnostics = errors.Select(err => new Diagnostic
        {
            Message = err.Message,
            Severity = err.Severity switch
            {
                AeroSeverity.Warning => DiagnosticSeverity.Warning,
                AeroSeverity.Info => DiagnosticSeverity.Information,
                AeroSeverity.Hint => DiagnosticSeverity.Hint,
                _ => DiagnosticSeverity.Error
            },
            Range = new Range(
                new Position(err.Span.Start.Line, err.Span.Start.Column),
                new Position(err.Span.End.Line, err.Span.End.Column)
            ),
            Source = "luft"
        }).ToList();

        _router.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = uri,
            Diagnostics = new Container<Diagnostic>(lspDiagnostics)
        });
    }
    
    protected override TextDocumentSyncRegistrationOptions CreateRegistrationOptions( TextSynchronizationCapability syncCapability, ClientCapabilities capability)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("aero"),
            Change = TextDocumentSyncKind.Full
        };
}