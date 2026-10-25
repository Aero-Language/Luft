using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Utility;

namespace Luft.TypeChecker.Symbols;

public sealed class FunctionSymbol(string name, AccessMod access, ModuleSymbol module, FunctionDeclarationNode declaration) : SourceSymbol(declaration.Span), ISignature
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

    public bool IsExtern => MemberMods.HasFlag(MemberMod.Extern);

    // The @Library("library", "symbol") of a native function, null for any other
    public AnnotationStatementNode? LibraryAnnotation => IsExtern ? Declaration.Annotations.FirstOrDefault(a => a.Name == "Library") : null;
    public string? NativeLibrary => LibraryArgument(0);
    public string? NativeSymbol => LibraryArgument(1) ?? (LibraryAnnotation is null ? null : Name);

    private string? LibraryArgument(int index)
        => LibraryAnnotation is { } library && index < library.Parameters.Count && library.Parameters[index] is LiteralExpressionNode { Value: string text } ? text : null;
    
    
    public SymbolSignature Signature => new(Name, GenericParameters, Parameters);
}

public sealed record ParamSymbol(string Name, AeroType Type, VariableKind VarKind, ExpressionNode? Initializer);

// An `op fun` declaration — overloads a specific Operator rather than being named. An instance
// overload has self as its left operand: one parameter is binary, none is unary. A static overload
// (declared `static`, or with two parameters) takes every operand as a parameter: two are binary,
// one (needs `static`) is unary. BodyResolver's FindOperatorOverload picks between them.
public sealed class OperatorSymbol(Operator op, AccessMod access, ModuleSymbol module, OperatorDeclarationNode declaration) : SourceSymbol(declaration.Span), ISignature
{
    public Operator Op { get; } = op;
    public AccessMod Access { get; } = access;
    public ModuleSymbol Module { get; } = module;
    public OperatorDeclarationNode Declaration { get; } = declaration;

    public MemberMod MemberMods => Declaration.MemberMods;
    public InheritanceMod InheritanceMod => Declaration.InheritanceMod;
    public bool IsStatic => MemberMods.HasFlag(MemberMod.Static) || Declaration.Parameters.Count == 2;

    public ValueList<ParamSymbol> Parameters => Declaration.Parameters
        .Select(p => new ParamSymbol(p.Name, p.Type, p.VarKind, p.Initializer)).ToValueList();

    public AeroType ReturnType => Declaration.ReturnType;
    public BlockExpressionNode? Body => Declaration.Body;

    // Operators aren't named, so the signature is keyed by the Operator itself; two overloads
    // of the same operator still collide here if their parameter lists also match (CheckDupes
    // in TypeResolver uses this to flag that).
    public SymbolSignature Signature => new($"<operator>{Op}", [], Parameters);
}

public sealed class PropertySymbol(string name, AccessMod access, PropertyDeclarationNode declaration) : SourceSymbol(declaration.Span), ISignature
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

public sealed class FieldSymbol(string name, AccessMod access, FieldDeclarationNode declaration) : SourceSymbol(declaration.Span), ISignature
{
    public string Name { get; } = name;
    public AccessMod Access { get; } = access;
    public FieldDeclarationNode Declaration { get; } = declaration;

    public VariableKind VarKind => Declaration.VarKind;
    public AeroType Type => Declaration.Type;
    public ExpressionNode? Initializer => Declaration.Initializer;
    
    
    public SymbolSignature Signature => new(Name, [], []);
}

public sealed class ConstructorSymbol(AccessMod access, ConstructorDeclarationNode declaration) : SourceSymbol(declaration.Span), ISignature
{
    public AccessMod Access { get; } = access;
    public ConstructorDeclarationNode Declaration { get; } = declaration;
    public ValueList<ParamSymbol> Parameters => Declaration.Parameters.Select(p => new ParamSymbol(p.Name, p.Type, p.VarKind, p.Initializer)).ToValueList();
    public BlockExpressionNode? Body => Declaration.Body;
    
    
    public SymbolSignature Signature => new($"<constructor>{Declaration.Name}", [], Parameters);
}

public sealed class DestructorSymbol(DestructorDeclarationNode declaration) : SourceSymbol(declaration.Span), ISignature
{
    public DestructorDeclarationNode Declaration { get; } = declaration;
    public BlockExpressionNode? Body => Declaration.Body;
    
    public SymbolSignature Signature => new($"<destructor>{Declaration.Name}", [], []);
}