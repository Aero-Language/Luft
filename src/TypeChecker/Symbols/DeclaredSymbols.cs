using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Utility;

namespace Luft.TypeChecker.Symbols;

public sealed class ClassSymbol(string name, AccessMod access, ModuleSymbol module, TypeScope? containing, SourceSpan span)
    : TypeSymbol(name, access, module, containing, span)
{
    public InheritanceMod InheritanceMod { get; init; }
    // Raw AeroTypes from `: A, B, C` - which one (if any) is the base class vs. a trait
    // is sorted out during linking, once every type in the table is known.
    public ValueList<AeroType> Implements { get; init; } = ValueList<AeroType>.Empty;
}

public sealed class StructSymbol(string name, AccessMod access, ModuleSymbol module, TypeScope? containing, SourceSpan span)
    : TypeSymbol(name, access, module, containing, span)
{
    public ValueList<AeroType> Implements { get; init; } = ValueList<AeroType>.Empty;
}

public sealed class TraitSymbol(string name, AccessMod access, ModuleSymbol module, TypeScope? containing, SourceSpan span)
    : TypeSymbol(name, access, module, containing, span)
{
    public ValueList<AeroType> Traits { get; init; } = ValueList<AeroType>.Empty; // traits this trait itself extends
}

public sealed class RecordSymbol(string name, AccessMod access, ModuleSymbol module, TypeScope? containing, SourceSpan span)
    : TypeSymbol(name, access, module, containing, span)
{
    public ValueList<AeroType> Implements { get; init; } = ValueList<AeroType>.Empty;
}

public sealed class AnnotationSymbol(string name, AccessMod access, ModuleSymbol module, TypeScope? containing, SourceSpan span)
    : TypeSymbol(name, access, module, containing, span);

public sealed class EnumSymbol(string name, AccessMod access, ModuleSymbol module, TypeScope? containing, SourceSpan span)
    : TypeSymbol(name, access, module, containing, span)
{
    public bool IsEnumClass { get; init; }
    public AeroType? MemberType { get; init; }
    public ValueList<ParamSymbol> Parameters { get; init; } = ValueList<ParamSymbol>.Empty;
    public List<EnumMemberSymbol> Members { get; } = new();
}

public sealed record EnumMemberSymbol(string Name, ExpressionNode? Value, AstNode Declaration);