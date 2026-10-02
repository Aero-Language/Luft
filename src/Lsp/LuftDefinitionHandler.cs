using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Luft.Lsp;

public class LuftDefinitionHandler(LuftCompilerService compiler) : DefinitionHandlerBase
{
    public override Task<LocationOrLocationLinks> Handle(DefinitionParams request, CancellationToken cancellationToken)
    {
        var none = new LocationOrLocationLinks(Array.Empty<LocationOrLocationLink>());

        try
        {
            var analyzer = new Analyzer(compiler.Current);
            var path = LuftCompilerService.PathOf(request.TextDocument.Uri);
            var hit = analyzer.Resolve(path, LspUtil.ToLocation(request.Position));
            if (hit?.Decl is not { } decl || decl.IsUnknown) return Task.FromResult(none);

            var location = new Location
            {
                Uri = DocumentUri.FromFileSystemPath(decl.FilePath),
                Range = LspUtil.ToRange(analyzer.NameSpan(decl, hit.Name))
            };
            return Task.FromResult(new LocationOrLocationLinks(new[] { new LocationOrLocationLink(location) }));
        }
        catch (Exception)
        {
            return Task.FromResult(none);
        }
    }

    protected override DefinitionRegistrationOptions CreateRegistrationOptions(DefinitionCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("aero")
        };
}