using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Utility;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Luft.Lsp;

public class LuftDocumentSymbolHandler(LuftCompilerService compiler) : DocumentSymbolHandlerBase
{
    public override Task<SymbolInformationOrDocumentSymbolContainer> Handle(DocumentSymbolParams request, CancellationToken cancellationToken)
    {
        var empty = new SymbolInformationOrDocumentSymbolContainer(Array.Empty<SymbolInformationOrDocumentSymbol>());

        try
        {
            var path = LuftCompilerService.PathOf(request.TextDocument.Uri);
            if (!compiler.Current.Files.TryGetValue(path, out var file)) return Task.FromResult(empty);

            var symbols = file.Ast.Modules
                .SelectMany(m => Symbols(m, false))
                .Select(s => new SymbolInformationOrDocumentSymbol(s));
            return Task.FromResult(new SymbolInformationOrDocumentSymbolContainer(symbols));
        }
        catch (Exception)
        {
            return Task.FromResult(empty);
        }
    }

    static DocumentSymbol Make(string name, string? detail, SymbolKind kind, SourceSpan span, IEnumerable<DocumentSymbol>? children = null)
    {
        var range = LspUtil.ToRange(span);
        return new DocumentSymbol
        {
            Name = string.IsNullOrWhiteSpace(name) ? "<anonymous>" : name,
            Detail = detail,
            Kind = kind,
            Range = range,
            SelectionRange = range,
            Children = children is null ? null : new Container<DocumentSymbol>(children)
        };
    }

    static IEnumerable<DocumentSymbol> Members(IEnumerable<DeclarationNode> decls) => decls.SelectMany(d => Symbols(d, true));

    static string Params(IEnumerable<ParamNode> parameters) => string.Join(", ", parameters.Select(p => $"{p.Name}: {p.Type}"));

    static IEnumerable<DocumentSymbol> Symbols(DeclarationNode node, bool inType)
    {
        switch (node)
        {
            case ModuleDeclarationNode mod:
                var inner = mod.Declarations.SelectMany(d => Symbols(d, false)).ToList();
                // The unnamed module only holds global declarations, so they sit at the top level
                if (mod.ModulePath == "") return inner;

                // Module spans are unreliable, so cover the children instead
                var decls = mod.Declarations;
                var span = decls.Length > 0 ? decls[0].Span.To(decls[^1].Span) : mod.Span;
                return [Make(mod.ModulePath, null, SymbolKind.Module, span, inner)];

            case FunctionDeclarationNode f:
                return [Make(f.Name, $"({Params(f.Parameters)}) -> {f.ReturnType}", inType ? SymbolKind.Method : SymbolKind.Function, f.Span)];
            case OperatorDeclarationNode o:
                return [Make($"operator {o.Op}", $"({Params(o.Parameters)}) -> {o.ReturnType}", SymbolKind.Operator, o.Span)];
            case ClassDeclarationNode c:
                return [Make(c.Name, null, SymbolKind.Class, c.Span, Members(c.Declarations))];
            case StructDeclarationNode s:
                return [Make(s.Name, null, SymbolKind.Struct, s.Span, Members(s.Declarations))];
            case RecordDeclarationNode r:
                return [Make(r.Name, null, SymbolKind.Struct, r.Span, Members(r.Declarations))];
            case TraitDeclarationNode t:
                return [Make(t.Name, null, SymbolKind.Interface, t.Span, Members(t.Declarations))];
            case AnnotationDeclarationNode a:
                return [Make(a.Name, null, SymbolKind.Class, a.Span, Members(a.Declarations))];
            case EnumDeclarationNode e:
                return [Make(e.Name, null, SymbolKind.Enum, e.Span, e.Members.Select(m => Make(m.Name, null, SymbolKind.EnumMember, m.Span)))];
            case PropertyDeclarationNode p:
                return [Make(p.Name, p.Type.ToString(), SymbolKind.Property, p.Span)];
            case FieldDeclarationNode fd:
                return [Make(fd.Name, fd.Type.IsAuto ? null : fd.Type.ToString(), fd.VarKind == VariableKind.Const ? SymbolKind.Constant : SymbolKind.Field, fd.Span)];
            case PrimaryConstructorDeclarationNode pc:
                return pc.Variables.Select(v => Make(v.Name, v.Type.ToString(), SymbolKind.Field, v.Span)).ToList();
            case ConstructorDeclarationNode co:
                return [Make($"constructor {co.Name}", $"({Params(co.Parameters)})", SymbolKind.Constructor, co.Span)];
            case DestructorDeclarationNode de:
                return [Make($"destructor {de.Name}", null, SymbolKind.Method, de.Span)];
            case ExtensionDeclarationNode ex:
                return Symbols(ex.Extension, inType);
            case ExtensionBlockDeclarationNode eb:
                return [Make($"extensions {eb.TargetType}", null, SymbolKind.Namespace, eb.Span, eb.Extensions.SelectMany(x => Symbols(x, true)))];
            default:
                return [];
        }
    }

    protected override DocumentSymbolRegistrationOptions CreateRegistrationOptions(DocumentSymbolCapability capability, ClientCapabilities clientCapabilities)
        => new()
        {
            DocumentSelector = TextDocumentSelector.ForLanguage("aero")
        };
}