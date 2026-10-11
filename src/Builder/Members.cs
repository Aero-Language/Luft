using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

public sealed record GlobalVariable(LLVMValueRef Address, LLVMTypeRef Type, AeroType AeroType);

public readonly record struct AccessorKey(PropertySymbol Property, bool IsSetter);

// Runs once at startup and stores a static's initial value
public sealed record StaticInitializer(GlobalVariable Target, ExpressionNode Value, TypeScope Scope, SourceSpan Span);

// Statics, module level fields and properties, declared once and shared by every FunctionEmitter
public sealed class MemberTable
{
    // Static, const and module level fields
    public Dictionary<FieldSymbol, GlobalVariable> Fields { get; } = new();

    // Static and module level properties, Backing only has the ones that need storage
    public HashSet<PropertySymbol> GlobalProperties { get; } = new();
    public Dictionary<PropertySymbol, GlobalVariable> Backing { get; } = new();

    public Dictionary<AccessorKey, DeclaredFunction> Accessors { get; } = new();
    public List<StaticInitializer> Initializers { get; } = [];
}
