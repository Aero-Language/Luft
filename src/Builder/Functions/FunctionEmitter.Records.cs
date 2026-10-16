using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

// Records compare field by field unless they declare their own 'Equality'
public sealed partial class FunctionEmitter
{
    private bool TryEmitRecordEquality(Operator op, RecordSymbol record, LLVMValueRef left, LLVMValueRef right, SourceSpan span, out LLVMValueRef value)
    {
        value = default;
        if (!TryEmitValueEquality(record, left, right, span, out var equal)) return false;

        value = op == Operator.Inequality ? _builder.BuildXor(equal, LLVMValueRef.CreateConstInt(_context.Int8Type, 1), "ne") : equal;
        return true;
    }

    // Bool (i8) telling whether two values of a record or struct type are equal, inherited fields included
    private bool TryEmitValueEquality(TypeSymbol type, LLVMValueRef left, LLVMValueRef right, SourceSpan span, out LLVMValueRef value)
    {
        value = default;
        if (FindEqualityOverload(type) is { } overload) return TryCallEquality(overload, left, right, span, out value);

        var layout = _types.LayoutOf(type, span);
        if (layout is null) return false;

        var all = LLVMValueRef.CreateConstInt(_context.Int8Type, 1);
        foreach (var slot in layout.Slots)
        {
            var a = _builder.BuildExtractValue(left, (uint)slot.Index, slot.Name);
            var b = _builder.BuildExtractValue(right, (uint)slot.Index, slot.Name);

            LLVMValueRef same;
            if (!slot.Type.IsNullable && _types.SymbolOf(slot.Type, type.Scope) is RecordSymbol or StructSymbol)
            {
                if (!TryEmitValueEquality(_types.SymbolOf(slot.Type, type.Scope)!, a, b, span, out same)) return false;
            }
            else if (!TryEmitComparison(Operator.Equality, a, slot.Type, b, slot.Type, span, out same)) return false;

            all = _builder.BuildAnd(all, same, "eq");
        }

        value = all;
        return true;
    }

    private static bool IsType(AeroType type, TypeSymbol symbol)
        => type is ScalarType { IsNullable: false } && (type.Name == symbol.Name || type.Name.EndsWith("." + symbol.Name));

    // An 'Equality' declared on the type itself that takes the same type and returns Bool
    private static OperatorSymbol? FindEqualityOverload(TypeSymbol type)
    {
        if (!type.Scope.Operators.TryGetValue(Operator.Equality, out var overloads)) return null;

        return overloads.FirstOrDefault(o => o.ReturnType == AeroType.Bool && (o.IsStatic
            ? o.Parameters.Count == 2 && IsType(o.Parameters[0].Type, type) && IsType(o.Parameters[1].Type, type)
            : o.Parameters.Count == 1 && IsType(o.Parameters[0].Type, type)));
    }

    private bool TryCallEquality(OperatorSymbol overload, LLVMValueRef left, LLVMValueRef right, SourceSpan span, out LLVMValueRef value)
    {
        value = default;
        if (!TryGetOperator(overload, span, out var declared)) return false;

        LLVMValueRef[] arguments;
        if (overload.IsStatic) arguments = [left, right];
        else
        {
            // The left value is not in memory, so self points at a copy
            var self = EntryAlloca(left.TypeOf, "self");
            _builder.BuildStore(left, self);
            arguments = [self, right];
        }

        value = _builder.BuildCall2(declared.Type, declared.Value, arguments, "eq");
        return true;
    }

    // A copy of the record value with the listed slots replaced, the source itself stays untouched
    private bool TryLowerWith(WithExpressionNode expression, out LLVMValueRef value)
    {
        value = default;
        var sourceType = TypeOf(expression.Source);
        if (_types.SymbolOf(sourceType, _scope) is not RecordSymbol record)
        {
            Fail("'with' can only copy a record", expression.Span);
            return false;
        }

        var layout = _types.LayoutOf(record, expression.Span);
        if (layout is null || !TryLowerValue(expression.Source, out var copy)) return false;

        foreach (var field in expression.Fields)
        {
            var slot = layout.Find(field.Name) ?? layout.Find($"<backing>{field.Name}");
            if (slot is null)
            {
                Fail($"'{field.Name}' has no storage in '{record.Name}' to replace", field.Span);
                return false;
            }

            if (!TryLowerValue(field.Value, out var replacement)) return false;
            replacement = Coerce(replacement, TypeOf(field.Value), slot.Type, field.Value.Span);
            copy = _builder.BuildInsertValue(copy, replacement, (uint)slot.Index, field.Name);
        }

        value = copy;
        return true;
    }
}
