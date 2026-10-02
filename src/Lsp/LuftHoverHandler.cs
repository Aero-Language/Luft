using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Luft.Lsp;

public class LuftHoverHandler(LuftCompilerService compiler) : HoverHandlerBase
{
    public override Task<Hover?> Handle(HoverParams request, CancellationToken cancellationToken)
    {
        try
        {
            var path = LuftCompilerService.PathOf(request.TextDocument.Uri);
            var hit = new Analyzer(compiler.Current).Resolve(path, LspUtil.ToLocation(request.Position));
            if (hit is null) return Task.FromResult<Hover?>(null);

            return Task.FromResult<Hover?>(new Hover
            {
                Contents = new MarkedStringsOrMarkupContent(new MarkupContent
                {
                    Kind = MarkupKind.Markdown,
                    Value = $"```aero\n{hit.Detail}\n```"
                }),
                Range = LspUtil.ToRange(hit.Ref)
            });
        }
        catch (Exception)
        {
            return Task.FromResult<Hover?>(null);
        }
    }

    protected override HoverRegistrationOptions CreateRegistrationOptions(HoverCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("aero")
        };
}