using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Luft.Lsp;

public class LuftCompletionHandler(LuftCompilerService compiler) : CompletionHandlerBase
{
    public override Task<CompletionList> Handle(CompletionParams request, CancellationToken cancellationToken)
    {
        try
        {
            var path = LuftCompilerService.PathOf(request.TextDocument.Uri);
            var items = new Analyzer(compiler.Current)
                .Complete(path, LspUtil.ToLocation(request.Position))
                .Select(s => new CompletionItem
                {
                    Label = s.Label,
                    Kind = KindOf(s.Kind),
                    Detail = s.Detail,
                    SortText = $"{s.Rank}_{s.Label}"
                })
                .ToList();

            return Task.FromResult(new CompletionList(items));
        }
        catch (Exception)
        {
            return Task.FromResult(new CompletionList());
        }
    }

    public override Task<CompletionItem> Handle(CompletionItem request, CancellationToken cancellationToken) => Task.FromResult(request);

    static CompletionItemKind KindOf(string kind) => kind switch
    {
        "keyword" => CompletionItemKind.Keyword,
        "module" => CompletionItemKind.Module,
        "class" or "annotation" or "type" => CompletionItemKind.Class,
        "struct" or "record" => CompletionItemKind.Struct,
        "trait" => CompletionItemKind.Interface,
        "enum" or "enum class" => CompletionItemKind.Enum,
        "enum member" => CompletionItemKind.EnumMember,
        "function" => CompletionItemKind.Function,
        "method" or "extension fun" => CompletionItemKind.Method,
        "property" => CompletionItemKind.Property,
        "field" => CompletionItemKind.Field,
        "constant" or "const" => CompletionItemKind.Constant,
        "type parameter" => CompletionItemKind.TypeParameter,
        _ => CompletionItemKind.Variable
    };

    protected override CompletionRegistrationOptions CreateRegistrationOptions(CompletionCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("aero"),
            TriggerCharacters = new Container<string>(".")
        };
}