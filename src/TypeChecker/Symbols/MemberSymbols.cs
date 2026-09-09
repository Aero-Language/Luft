using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Utility;

namespace Luft.TypeChecker.Symbols;

public sealed class FunctionSymbol(string name, AccessMod access, ModuleSymbol module, FunctionDeclarationNode declaration) : ISignature
{
    public string Name { get; } = name;
    public AccessMod Access { get; } = access;
    public ModuleSymbol Module { get; } = module;
    public FunctionDeclarationNode Declaration { get; } = declaration;

    public MemberMod MemberMods { get; init; }
    public InheritanceMod InheritanceMod { get; init; }
    public ValueList<GenericParameterType> GenericParameters { get; init; } = ValueList<GenericParameterType>.Empty;
    public ValueList<ParamSymbol> Parameters { get; init; } = ValueList<ParamSymbol>.Empty;
    public AeroType ReturnType { get; init; } = AeroType.Void; // omitted -> Void, per your own resolved default
    public BlockExpressionNode? Body { get; init; }            // null => trait member with no implementation

    public AeroType? ExtensionTarget { get; init; }             // set only for `extension fun Target.Name(...)`
    
    
    public SymbolSignature Signature => new(Name, GenericParameters, Parameters);
}

public sealed record ParamSymbol(string Name, AeroType Type, VariableKind VarKind, ExpressionNode? Initializer);

public sealed class PropertySymbol(string name, AccessMod access, PropertyDeclarationNode declaration)
{
    public string Name { get; } = name;
    public AccessMod Access { get; } = access;
    public PropertyDeclarationNode Declaration { get; } = declaration;

    public AeroType Type { get; init; } = AeroType.Error;
    public ExpressionNode? Initializer { get; init; }
    public PropertyAccessorSymbol? Getter { get; init; }
    public PropertyAccessorSymbol? Setter { get; init; }
    public AeroType? ExtensionTarget { get; init; }
    
    
    public SymbolSignature Signature => new(Name, [], []);
}

public sealed record PropertyAccessorSymbol(AccessMod Access, BlockExpressionNode? Body);

public sealed class FieldSymbol(string name, AccessMod access, FieldDeclarationNode declaration)
{
    public string Name { get; } = name;
    public AccessMod Access { get; } = access;
    public FieldDeclarationNode Declaration { get; } = declaration;

    public VariableKind VarKind { get; init; }
    public AeroType Type { get; init; } = AeroType.Auto;   // Auto => infer from Initializer in pass 2
    public ExpressionNode? Initializer { get; init; }
    
    
    public SymbolSignature Signature => new(Name, [], []);
}

public sealed class ConstructorSymbol(AccessMod access, ConstructorDeclarationNode declaration) : ISignature
{
    public AccessMod Access { get; } = access;
    public ConstructorDeclarationNode Declaration { get; } = declaration;
    public ValueList<ParamSymbol> Parameters { get; init; } = ValueList<ParamSymbol>.Empty;
    public BlockExpressionNode? Body { get; init; }
    
    
    public SymbolSignature Signature => new("", [], Parameters);
}

public sealed class DestructorSymbol(DestructorDeclarationNode declaration)
{
    public DestructorDeclarationNode Declaration { get; } = declaration;
    public BlockExpressionNode? Body { get; init; }
}