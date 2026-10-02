using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Luft.Lsp;

public class LuftSemanticTokensHandler(LuftCompilerService compiler) : SemanticTokensHandlerBase
{
    // Index in this list is what goes on the wire; the plugin maps these names to colors
    static readonly SemanticTokenType[] Types =
    [
        SemanticTokenType.Class,
        SemanticTokenType.Struct,
        new SemanticTokenType("record"),
        SemanticTokenType.Interface,
        SemanticTokenType.Enum,
        SemanticTokenType.EnumMember,
        SemanticTokenType.Property,
        new SemanticTokenType("field"),
        SemanticTokenType.Variable,
        SemanticTokenType.Parameter
    ];

    static readonly SemanticTokensLegend Legend = new()
    {
        TokenTypes = new Container<SemanticTokenType>(Types),
        TokenModifiers = new Container<SemanticTokenModifier>(Array.Empty<SemanticTokenModifier>())
    };

    static int TypeIndex(string kind) => kind switch
    {
        "class" or "annotation" => 0,
        "struct" => 1,
        "record" => 2,
        "trait" => 3,
        "enum" or "enum class" => 4,
        "enum member" => 5,
        "property" => 6,
        "field" or "constant" => 7,
        "val" or "var" or "const" => 8,
        "parameter" => 9,
        _ => -1
    };

    protected override Task Tokenize(SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier, CancellationToken cancellationToken)
    {
        try
        {
            var path = LuftCompilerService.PathOf(identifier.TextDocument.Uri);
            foreach (var (token, kind) in new Analyzer(compiler.Current).Classify(path))
            {
                var type = TypeIndex(kind);
                if (type < 0) continue;

                var start = LspUtil.ToPosition(token.Span.Start);
                builder.Push(start.Line, start.Character, token.Value.Length, type, 0);
            }
        }
        catch (Exception) { }

        return Task.CompletedTask;
    }

    protected override Task<SemanticTokensDocument> GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
        => Task.FromResult(new SemanticTokensDocument(Legend));

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(SemanticTokensCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("aero"),
            Legend = Legend,
            Full = new SemanticTokensCapabilityRequestFull { Delta = false }
        };
}