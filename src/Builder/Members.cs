using LLVMSharp.Interop;
using Luft.Ast;
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

    // Operator overloads, declared like methods with self first
    public Dictionary<OperatorSymbol, DeclaredFunction> Operators { get; } = new(ReferenceEqualityComparer.Instance);
    public List<StaticInitializer> Initializers { get; } = [];

    // Slot of every virtual method in its class's vtable (slot 0 is the destructor)
    public Dictionary<FunctionSymbol, int> VirtualSlots { get; } = new();

    // Per class: the vtable stored in each new object and the function that runs its destructor and its bases'
    public Dictionary<TypeSymbol, LLVMValueRef> VTables { get; } = new();
    public Dictionary<TypeSymbol, DeclaredFunction> Destroyers { get; } = new();

    // Per trait the methods a vtable lists (inherited traits first) and each method's position in it
    public Dictionary<TraitSymbol, List<FunctionSymbol>> TraitMethods { get; } = new();
    public Dictionary<(TraitSymbol Trait, FunctionSymbol Method), int> TraitSlots { get; } = new();

    // The vtable a class, struct or record hands out when it is used as a trait
    public Dictionary<(TypeSymbol Type, TraitSymbol Trait), LLVMValueRef> TraitVTables { get; } = new();
}

public sealed record VirtualSlot(string Key, FunctionSymbol Implementation);

public static class VirtualTables
{
    public static string KeyOf(FunctionSymbol function) => $"{function.Name}({string.Join(",", function.Parameters.Select(p => p.Type))})";

    private static bool IsVirtual(FunctionSymbol function)
        => function.InheritanceMod is InheritanceMod.Virtual or InheritanceMod.Abstract or InheritanceMod.Implements or InheritanceMod.Sealed
           && !function.MemberMods.HasFlag(MemberMod.Static)
           && function.GenericParameters.Count == 0;

    // The inherited slots come first and stay in place, an override replaces its slot's implementation, a new virtual or abstract method appends one.
    // `index` receives the vtable position (slot + 1) of every function that takes part.
    public static List<VirtualSlot> Of(ClassSymbol type, TypeLowering types, Dictionary<FunctionSymbol, int> index)
    {
        var slots = types.BaseOf(type) is ClassSymbol baseClass ? Of(baseClass, types, index) : new List<VirtualSlot>();

        foreach (var function in type.Scope.Functions.Values.SelectMany(l => l).OrderBy(f => f.Span.Start).Where(IsVirtual))
        {
            var key = KeyOf(function);
            var at = slots.FindIndex(s => s.Key == key);

            if (at >= 0) slots[at] = slots[at] with { Implementation = function };
            else if (function.InheritanceMod is InheritanceMod.Virtual or InheritanceMod.Abstract)
            {
                slots.Add(new VirtualSlot(key, function));
                at = slots.Count - 1;
            }
            else continue; // an override without anything to override, the checker reports that

            index[function] = at + 1;
        }

        return slots;
    }
}
