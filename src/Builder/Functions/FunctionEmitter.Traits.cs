using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

// A trait value is { data, vtable }: data is the object (a class) or a heap copy of the value (a struct or record), the vtable lists the trait's methods
public sealed partial class FunctionEmitter
{
    // Turns a class, struct or record into the trait it is passed or assigned as; false when the target is not a trait
    private bool TryConvertToTrait(LLVMValueRef value, AeroType from, AeroType to, SourceSpan span, out LLVMValueRef result)
    {
        result = value;
        if (to.IsNullable || to.IsRef || from.IsNullable) return false;
        if (_types.SymbolOf(to, _scope) is not TraitSymbol trait) return false;

        var source = _types.SymbolOf(from, _scope);
        if (source is not (ClassSymbol or StructSymbol or RecordSymbol)) return false;

        if (!_members.TraitVTables.TryGetValue((source, trait), out var table))
        {
            Fail($"'{source.Name}' does not implement the trait '{trait.Name}', or it cannot be used as one yet", span);
            return true;
        }

        var data = value;
        if (source is not ClassSymbol)
        {
            var layout = _types.LayoutOf(source, span);
            if (layout is null) return true;

            // The trait value may outlive this function, so a value type is copied to the heap
            data = CallRuntime("aero_alloc", _types.Ptr, layout.StructType.SizeOf);
            _builder.BuildStore(value, data);
        }

        var reference = _builder.BuildInsertValue(LLVMValueRef.CreateConstNull(_types.TraitStruct), data, 0, "trait");
        result = _builder.BuildInsertValue(reference, table, 1, "trait");
        return true;
    }

    // A call on a trait value goes through its vtable: self is the data pointer
    private bool TryLowerTraitCall(CallExpressionNode call, FunctionSymbol function, out bool handled, out LLVMValueRef value)
    {
        value = default;
        handled = false;

        var target = call.Target;
        while (target is ScopedExpressionNode s) target = s.Scoped;

        ExpressionNode? receiver = null;
        AeroType? receiverType;
        if (target is MemberAccessExpressionNode member)
        {
            receiver = member.Target;
            receiverType = TypeOf(member.Target);
        }
        else receiverType = _selfReceiver;

        if (receiverType is null || _types.SymbolOf(receiverType, _scope) is not TraitSymbol trait) return true;
        function = _types.MapMember(trait, function); // the checker bound the method of the generic definition
        if (!_members.TraitSlots.TryGetValue((trait, function), out var slot)) return true;

        handled = true;

        LLVMValueRef traitValue;
        if (receiver is null)
        {
            if (!TryLowerSelf(call.Span, out traitValue)) return false;
        }
        else if (!TryLowerValue(receiver, out traitValue)) return false;

        var data = _builder.BuildExtractValue(traitValue, 0, "data");
        var table = _builder.BuildExtractValue(traitValue, 1, "vtable");
        var entry = _builder.BuildGEP2(_types.Ptr, table, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)slot) }, "slot");
        var callee = _builder.BuildLoad2(_types.Ptr, entry, "method");

        // Parameter types of a generic trait's method are written with its generic names
        var arguments0 = _types.InfoOf(trait)?.Arguments;
        var parameters = function.Parameters.Select(p => p with { Type = TypeSubstitution.Apply(p.Type, arguments0) }).ToList();
        var returnType = TypeSubstitution.Apply(function.ReturnType, arguments0);

        var lowered = _types.LowerFunction(returnType, parameters.Select(p => p.Type), trait.Scope, call.Span);
        if (lowered is null) return false;
        var type = LLVMTypeRef.CreateFunction(lowered.Value.ReturnType, new[] { _types.Ptr }.Concat(lowered.Value.GetParamTypes()).ToArray(), false);

        var arguments = new List<LLVMValueRef> { data };
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var source = i < call.Arguments.Count ? call.Arguments[i] : parameter.Initializer;
            if (!TryLowerArgument(source, parameter.Type, parameter.Name, call.Span, out var argument)) return false;
            arguments.Add(argument);
        }

        var isVoid = returnType == AeroType.Void;
        var result = _builder.BuildCall2(type, callee, arguments.ToArray(), isVoid ? "" : "call");
        if (!isVoid) value = result;
        return true;
    }
}
