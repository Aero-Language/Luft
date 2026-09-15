using Luft.Ast.Nodes;
using Luft.Utility;

namespace Luft.TypeChecker.Symbols;

public sealed class ClassSymbol : TypeSymbol
{
    public ClassSymbol(ClassDeclarationNode node, ModuleSymbol module, TypeScope? containing) : base(node.Name,
        node.AccessMod, module, containing, node.Span)
    {
        Node = node;
        GenericParameters = node.GenericParameters;
    }
    
    public ClassDeclarationNode Node { get; }
}

public sealed class StructSymbol(StructDeclarationNode node, ModuleSymbol module, TypeScope? containing)
    : TypeSymbol(node.Name,
        node.AccessMod, module, containing, node.Span)
{
    public StructDeclarationNode Node { get; } = node;
}

public sealed class TraitSymbol(TraitDeclarationNode node, ModuleSymbol module, TypeScope? containing)
    : TypeSymbol(node.Name, node.AccessMod, module, containing, node.Span)
{
    public TraitDeclarationNode Node { get; } = node;
}

public sealed class RecordSymbol(RecordDeclarationNode node, ModuleSymbol module, TypeScope? containing)
    : TypeSymbol(node.Name, node.AccessMod, module, containing, node.Span)
{
    public RecordDeclarationNode Node { get; } = node;
}

public sealed class AnnotationSymbol(AnnotationDeclarationNode node, ModuleSymbol module, TypeScope? containing)
    : TypeSymbol(node.Name, node.AccessMod, module, containing, node.Span);

public sealed class EnumSymbol(EnumDeclarationNode node, ModuleSymbol module, TypeScope? containing)
    : TypeSymbol(node.Name, node.AccessMod, module, containing, node.Span)
{
    public bool IsEnumClass => node.IsEnumClass;
    public AeroType? MemberType => node.MemberType;
    public ValueList<ParamSymbol> Parameters => node.Parameters?.Select(p => new ParamSymbol(p.Name, p.Type, p.VarKind, p.Initializer))?.ToValueList() ?? [];

    public List<EnumMemberSymbol> Members => node.Members.Select(m => new EnumMemberSymbol(m.Name, m.Value, m)).ToList();
}

public sealed record EnumMemberSymbol(string Name, ExpressionNode? Value, AstNode Declaration);