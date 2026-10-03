using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Luft.Lsp;

// Only what needs symbol info: types and variables. Keywords, literals and comments stay with the editor's lexer.
public class LuftSemanticTokensHandler(LuftCompilerService compiler) : SemanticTokensHandlerBase
{
    static readonly string[] TokenTypes =
    [
        "class", "struct", "record", "interface", "enum", "enumMember", "typeParameter",
        "property", "field", "constant", "variable", "parameter"
    ];

    protected override Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        try
        {
            var path = LuftCompilerService.PathOf(identifier.TextDocument.Uri);
            // Tokens must be pushed in document order
            foreach (var token in new Analyzer(compiler.Current).Classify(path))
                builder.Push(LspUtil.ToRange(token.Span), token.Type);
        }
        catch (Exception) { }

        return Task.CompletedTask;
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
        => Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("aero"),
            Legend = new SemanticTokensLegend
            {
                TokenTypes = new Container<SemanticTokenType>(TokenTypes.Select(t => new SemanticTokenType(t))),
                TokenModifiers = new Container<SemanticTokenModifier>()
            },
            Full = new SemanticTokensCapabilityRequestFull { Delta = false },
            Range = false
        };
}