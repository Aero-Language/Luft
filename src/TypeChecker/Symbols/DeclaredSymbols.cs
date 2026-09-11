using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Utility;

namespace Luft.TypeChecker.Symbols;

public sealed class ClassSymbol(ClassDeclarationNode node, ModuleSymbol module, TypeScope? containing)
    : TypeSymbol(node.Name, node.AccessMod, module, containing, node.Span)
{
    public InheritanceMod InheritanceMod { get; init; } = node.InheritanceMod;
    // Raw AeroTypes from `: A, B, C` - which one (if any) is the base class vs. a trait
    // is sorted out during linking, once every type in the table is known.
    public ValueList<AeroType> Implements { get; init; } = node.Implements;
}

public sealed class StructSymbol(StructDeclarationNode node, ModuleSymbol module, TypeScope? containing)
    : TypeSymbol(node.Name, node.AccessMod, module, containing, node.Span)
{
    public ValueList<AeroType> Implements { get; init; } = node.Implements;
}

public sealed class TraitSymbol(TraitDeclarationNode node, ModuleSymbol module, TypeScope? containing)
    : TypeSymbol(node.Name, node.AccessMod, module, containing, node.Span)
{
    public ValueList<AeroType> Traits { get; init; } = node.Traits; // traits this trait itself extends
}

public sealed class RecordSymbol(RecordDeclarationNode node, ModuleSymbol module, TypeScope? containing)
    : TypeSymbol(node.Name, node.AccessMod, module, containing, node.Span)
{
    public ValueList<AeroType> Implements { get; init; } = node.Implements;
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