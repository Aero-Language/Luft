using Luft.Ast.Nodes;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.TypeChecker;

// What a name or call resolved to, recorded by BodyResolver for the code generator
public abstract record Binding;

public sealed record LocalBinding(VariableSymbol Variable) : Binding;
public sealed record FieldBinding(FieldSymbol Field) : Binding;
public sealed record PropertyBinding(PropertySymbol Property) : Binding;
public sealed record EnumMemberBinding(EnumSymbol Enum, string Name) : Binding;
public sealed record EnumParamBinding(EnumSymbol Enum, ParamSymbol Param) : Binding;
public sealed record TypeBinding(TypeSymbol Type) : Binding;
public sealed record ModuleBinding(string Path) : Binding;
public sealed record OperatorBinding(OperatorSymbol Operator) : Binding;

// TypeArguments binds the function's own generics, ReceiverTypeArguments the generics of the type it was accessed on
public sealed record FunctionBinding(
    FunctionSymbol Function,
    Dictionary<string, AeroType> TypeArguments,
    Dictionary<string, AeroType>? ReceiverTypeArguments) : Binding;

// Constructor is null for the primary or implicit constructor; the argument count tells them apart
public sealed record ConstructorBinding(TypeSymbol Type, ConstructorSymbol? Constructor) : Binding;

// Keyed by node reference, since AST records compare structurally
public sealed class TypedInfo
{
    public Dictionary<AstNode, AeroType> Types { get; } = new(ReferenceEqualityComparer.Instance);
    public Dictionary<AstNode, Binding> Bindings { get; } = new(ReferenceEqualityComparer.Instance);

    public AeroType? TypeOf(AstNode node) => Types.GetValueOrDefault(node);
    public Binding? BindingOf(AstNode node) => Bindings.GetValueOrDefault(node);
}