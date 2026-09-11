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

    public MemberMod MemberMods => Declaration.MemberMods;
    public InheritanceMod InheritanceMod => Declaration.InheritanceMod;
    public ValueList<GenericParameterType> GenericParameters => Declaration.GenericParameters;

    public ValueList<ParamSymbol> Parameters => Declaration.Parameters
        .Select(p => new ParamSymbol(p.Name, p.Type, p.VarKind, p.Initializer)).ToValueList();

    public AeroType ReturnType => Declaration.ReturnType;
    public BlockExpressionNode? Body => Declaration.Body;

    public AeroType? ExtensionTarget { get; init; } // set only for `extension fun Target.Name(...)`
    
    
    public SymbolSignature Signature => new(Name, GenericParameters, Parameters);
}

public sealed record ParamSymbol(string Name, AeroType Type, VariableKind VarKind, ExpressionNode? Initializer);

public sealed class PropertySymbol(string name, AccessMod access, PropertyDeclarationNode declaration)
{
    public string Name { get; } = name;
    public AccessMod Access { get; } = access;
    public PropertyDeclarationNode Declaration => declaration;

    public AeroType Type => Declaration.Type;
    public ExpressionNode? Initializer => Declaration.Initializer;
    public PropertyAccessorNode? Getter => Declaration.Getter;
    public PropertyAccessorNode? Setter => Declaration.Setter;
    public AeroType? ExtensionTarget { get; init; }
    
    
    public SymbolSignature Signature => new(Name, [], []);
}

public sealed class FieldSymbol(string name, AccessMod access, FieldDeclarationNode declaration)
{
    public string Name { get; } = name;
    public AccessMod Access { get; } = access;
    public FieldDeclarationNode Declaration { get; } = declaration;

    public VariableKind VarKind => Declaration.VarKind;
    public AeroType Type => Declaration.Type;
    public ExpressionNode? Initializer => Declaration.Initializer;
    
    
    public SymbolSignature Signature => new(Name, [], []);
}

public sealed class ConstructorSymbol(AccessMod access, ConstructorDeclarationNode declaration) : ISignature
{
    public AccessMod Access { get; } = access;
    public ConstructorDeclarationNode Declaration { get; } = declaration;
    public ValueList<ParamSymbol> Parameters => Declaration.Parameters.Select(p => new ParamSymbol(p.Name, p.Type, p.VarKind, p.Initializer)).ToValueList();
    public BlockExpressionNode? Body => Declaration.Body;
    
    
    public SymbolSignature Signature => new("", [], Parameters);
}

public sealed class DestructorSymbol(DestructorDeclarationNode declaration)
{
    public DestructorDeclarationNode Declaration { get; } = declaration;
    public BlockExpressionNode? Body => Declaration.Body;
}