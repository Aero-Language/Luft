using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Luft.Lsp;

public class LuftCompletionHandler : CompletionHandlerBase
{
    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        var items = new List<CompletionItem>
        {
            new CompletionItem
            {
                Label = "fn",
                Kind = CompletionItemKind.Keyword,
                Detail = "Function declaration",
                InsertText = "fn ${1:name}() -> ${2:Void} {\n\t$0\n}"
            },
            new CompletionItem
            {
                Label = "struct",
                Kind = CompletionItemKind.Keyword,
                Detail = "Struct type declaration"
            }
        };

        return Task.FromResult(new CompletionList(items));
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken)
    {
        return  Task.FromResult(request);
    }

    protected override CompletionRegistrationOptions CreateRegistrationOptions( CompletionCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("aero"),
            TriggerCharacters = new Container<string>(".")
        };
}