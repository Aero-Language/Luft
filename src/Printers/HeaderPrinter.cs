using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.Utility;

namespace Luft.Printers;

// Prints the public API of a file: declarations only, no code, no default values, no layouts.
// Headers are read by the checker and the language server, the binary holds everything else.
public sealed class HeaderPrinter(TypedInfo? typed = null) : PrettyPrinter(4, true)
{
    public string PrintHeader(FileNode file) => Print(StripFile(file));

    // A declaration without a body ends with ';'
    protected override string VisitFunction(FunctionDeclarationNode node) => Terminate(base.VisitFunction(node), node.Body);
    protected override string VisitOperator(OperatorDeclarationNode node) => Terminate(base.VisitOperator(node), node.Body);
    protected override string VisitConstructor(ConstructorDeclarationNode node) => Terminate(base.VisitConstructor(node), node.Body);

    private static string Terminate(string text, BlockExpressionNode? body) => body is null ? text + ";" : text;

    private FileNode StripFile(FileNode file) => file with { Modules = file.Modules.Select(m => m with { Declarations = m.Declarations.SelectMany(d => Declaration(d, false)).ToArray() }).ToArray() };

    // `inTrait` members are part of the API whatever their access says
    private IEnumerable<DeclarationNode> Declaration(DeclarationNode node, bool inTrait)
    {
        switch (node)
        {
            case FunctionDeclarationNode f when inTrait || f.AccessMod == AccessMod.Public:
                foreach (var parameters in Overloads(f.Parameters))
                    yield return f with { Body = null, Parameters = parameters, AccessMod = inTrait ? AccessMod.Public : f.AccessMod };
                break;

            case OperatorDeclarationNode o when o.AccessMod == AccessMod.Public:
                yield return o with { Body = null, Parameters = Overloads(o.Parameters).First() };
                break;

            case ExtensionDeclarationNode e:
                foreach (var inner in Declaration(e.Extension, false)) yield return e with { Extension = inner };
                break;

            case ExtensionBlockDeclarationNode b when b.AccessMod == AccessMod.Public:
                yield return b with { Extensions = b.Extensions.SelectMany(x => Declaration(x, false)).OfType<ExtensionDeclarationNode>().ToValueList() };
                break;

            case ClassDeclarationNode c when c.AccessMod == AccessMod.Public:
                yield return c with { Declarations = Members(c.Declarations, c.Name, false), BaseArguments = null };
                break;
            case StructDeclarationNode s when s.AccessMod == AccessMod.Public:
                yield return s with { Declarations = Members(s.Declarations, s.Name, false), BaseArguments = null };
                break;
            case RecordDeclarationNode r when r.AccessMod == AccessMod.Public:
                yield return r with { Declarations = Members(r.Declarations, r.Name, false), BaseArguments = null };
                break;
            case TraitDeclarationNode t when t.AccessMod == AccessMod.Public:
                yield return t with { Declarations = Members(t.Declarations, t.Name, true) };
                break;
            case AnnotationDeclarationNode a when a.AccessMod == AccessMod.Public:
                yield return a with { Declarations = Members(a.Declarations, a.Name, false) };
                break;

            // The order of the members is their ordinal, their values are code
            case EnumDeclarationNode en when en.AccessMod == AccessMod.Public:
                yield return en with
                {
                    Parameters = en.Parameters?.Select(Strip).ToValueList(),
                    Members = en.Members.Select(m => m with { Value = null }).ToValueList()
                };
                break;

            case PropertyDeclarationNode p when inTrait || p.AccessMod == AccessMod.Public:
                yield return p with
                {
                    Type = Known(p.Type, p.Initializer),
                    Initializer = null,
                    Getter = p.Getter is null ? null : p.Getter with { Body = null },
                    Setter = p.Setter is null ? null : p.Setter with { Body = null }
                };
                break;

            case FieldDeclarationNode f when f.AccessMod == AccessMod.Public:
                yield return f with { Type = Known(f.Type, f.Initializer), Initializer = null };
                break;
        }
    }

    private ValueList<DeclarationNode> Members(ValueList<DeclarationNode> declarations, string typeName, bool inTrait)
    {
        var result = new List<DeclarationNode>();

        foreach (var declaration in declarations)
        {
            switch (declaration)
            {
                case PrimaryConstructorDeclarationNode primary:
                    result.Add(primary with { Variables = primary.Variables.Select(v => v with { Type = Known(v.Type, v.Initializer), Initializer = null }).ToValueList() });

                    // The shorter argument lists of a primary constructor with defaults become explicit constructors
                    var variables = primary.Variables;
                    var optional = TrailingDefaults(variables.Select(v => v.Initializer));
                    for (var count = variables.Count - 1; count >= variables.Count - optional; count--)
                    {
                        var parameters = variables.Take(count).Select(v => new ParamNode(v.Name, Known(v.Type, v.Initializer), v.Span, null, v.VarKind)).ToValueList();
                        result.Add(new ConstructorDeclarationNode(ValueList<AnnotationStatementNode>.Empty, AccessMod.Public, typeName, parameters, null, primary.Span));
                    }
                    break;

                case ConstructorDeclarationNode constructor when constructor.AccessMod == AccessMod.Public:
                    foreach (var parameters in Overloads(constructor.Parameters))
                        result.Add(constructor with { Body = null, Parameters = parameters, BaseArguments = null });
                    break;

                case DestructorDeclarationNode:
                    break;

                default:
                    result.AddRange(Declaration(declaration, inTrait));
                    break;
            }
        }

        return result.ToValueList();
    }

    // The full parameter list without defaults, then one list per shorter call a default allows
    private IEnumerable<ValueList<ParamNode>> Overloads(ValueList<ParamNode> parameters)
    {
        var stripped = parameters.Select(Strip).ToValueList();
        yield return stripped;

        var optional = TrailingDefaults(parameters.Select(p => p.Initializer));
        for (var count = parameters.Count - 1; count >= parameters.Count - optional; count--)
            yield return stripped.Take(count).ToValueList();
    }

    private static int TrailingDefaults(IEnumerable<ExpressionNode?> initializers)
    {
        var count = 0;
        foreach (var initializer in initializers.Reverse())
        {
            if (initializer is null) break;
            count++;
        }
        return count;
    }

    private ParamNode Strip(ParamNode p) => p with { Initializer = null, Type = Known(p.Type, p.Initializer) };

    // A type that was left out and inferred from the initializer has to be written down, the initializer is gone
    private AeroType Known(AeroType type, ExpressionNode? initializer)
        => type.IsAuto && initializer is not null && typed?.TypeOf(initializer) is { IsAuto: false, IsError: false } inferred ? inferred : type;
}
