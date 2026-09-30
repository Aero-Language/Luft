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

public sealed class StructSymbol : TypeSymbol
{
    public StructSymbol(StructDeclarationNode node, ModuleSymbol module, TypeScope? containing) : base(node.Name,
        node.AccessMod, module, containing, node.Span)
    {
        Node = node;
        GenericParameters = node.GenericParameters;
    }

    public StructDeclarationNode Node { get; }
}

public sealed class TraitSymbol : TypeSymbol
{
    public TraitSymbol(TraitDeclarationNode node, ModuleSymbol module, TypeScope? containing) : base(node.Name,
        node.AccessMod, module, containing, node.Span)
    {
        Node = node;
        GenericParameters = node.GenericParameters;
    }

    public TraitDeclarationNode Node { get; }
}

public sealed class RecordSymbol : TypeSymbol
{
    public RecordSymbol(RecordDeclarationNode node, ModuleSymbol module, TypeScope? containing) : base(node.Name,
        node.AccessMod, module, containing, node.Span)
    {
        Node = node;
        GenericParameters = node.GenericParameters;
    }

    public RecordDeclarationNode Node { get; }
}

public sealed class AnnotationSymbol : TypeSymbol
{
    public AnnotationSymbol(AnnotationDeclarationNode node, ModuleSymbol module, TypeScope? containing) : base(node.Name,
        node.AccessMod, module, containing, node.Span)
    {
        GenericParameters = node.GenericParameters;
    }
}

public sealed class EnumSymbol(EnumDeclarationNode node, ModuleSymbol module, TypeScope? containing)
    : TypeSymbol(node.Name, node.AccessMod, module, containing, node.Span)
{
    // Enums can't have generic parameters at all (TypeResolver.CheckTypeSymbol errors if they
    // do), so GenericParameters correctly stays at its Empty default here.
    public bool IsEnumClass => node.IsEnumClass;
    public AeroType? MemberType => node.MemberType;
    public ValueList<ParamSymbol> Parameters => node.Parameters?.Select(p => new ParamSymbol(p.Name, p.Type, p.VarKind, p.Initializer))?.ToValueList() ?? [];

    public List<EnumMemberSymbol> Members => node.Members.Select(m => new EnumMemberSymbol(m.Name, m.Value, m)).ToList();
}

public sealed record EnumMemberSymbol(string Name, ExpressionNode? Value, AstNode Declaration);