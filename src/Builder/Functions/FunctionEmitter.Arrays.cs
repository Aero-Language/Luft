using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.Utility;

namespace Luft.Builder;

// An array is { data pointer, length }; the buffer lives on the heap and is shared when the array value is copied
public sealed partial class FunctionEmitter
{
    private bool TryLowerArrayLiteral(ArrayLiteralExpressionNode literal, out LLVMValueRef value)
    {
        value = default;

        var array = LLVMValueRef.CreateConstNull(_types.ArrayStruct);
        if (literal.Elements.Count == 0)
        {
            value = array; // null data, length 0
            return true;
        }

        if (_typed.TypeOf(literal) is not ArrayType type)
        {
            Fail("The array literal was not resolved", literal.Span);
            return false;
        }
        if (!_types.TryLower(type.ElementType, _scope, literal.Span, out var elementType)) return false;

        var count = (ulong)literal.Elements.Count;
        var bytes = _builder.BuildMul(elementType.SizeOf, LLVMValueRef.CreateConstInt(_context.Int64Type, count), "bytes");
        var data = CallRuntime("aero_alloc", _types.Ptr, bytes);

        for (var i = 0; i < literal.Elements.Count; i++)
        {
            var element = literal.Elements[i];
            if (!TryLowerValue(element, out var item)) return false;

            var slot = _builder.BuildGEP2(elementType, data, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)i) }, "elem");
            _builder.BuildStore(Coerce(item, _typed.TypeOf(element), type.ElementType, element.Span), slot);
        }

        array = _builder.BuildInsertValue(array, data, 0, "array");
        value = _builder.BuildInsertValue(array, LLVMValueRef.CreateConstInt(_context.Int32Type, count), 1, "array");
        return true;
    }

    // Checks the index against the length, an out of range access stops the program
    private bool TryLowerElementAddress(IndexExpressionNode index, out LLVMValueRef address, out LLVMTypeRef elementType, out AeroType elementAeroType)
    {
        address = default;
        elementType = default;
        elementAeroType = AeroType.Error;

        if (_typed.TypeOf(index.Target) is not ArrayType array)
        {
            Fail("Only arrays can be indexed", index.Span);
            return false;
        }
        elementAeroType = array.ElementType;

        if (!TryLowerValue(index.Target, out var target) || !TryLowerValue(index.Index, out var position)) return false;
        if (!_types.TryLower(array.ElementType, _scope, index.Span, out elementType)) return false;

        var data = _builder.BuildExtractValue(target, 0, "data");
        var length = _builder.BuildExtractValue(target, 1, "length");

        var inRange = _builder.BuildICmp(LLVMIntPredicate.LLVMIntULT, position, length, "inrange");
        var ok = AppendBlock("index.ok");
        var fail = AppendBlock("index.fail");
        _builder.BuildCondBr(inRange, ok, fail);

        _builder.PositionAtEnd(fail);
        CallRuntime("aero_array_oob", _context.VoidType, position, length);
        _builder.BuildUnreachable();

        _builder.PositionAtEnd(ok);
        address = _builder.BuildGEP2(elementType, data, new[] { position }, "elem");
        return true;
    }

    private bool TryLowerIndex(IndexExpressionNode index, out LLVMValueRef value)
    {
        value = default;
        if (!TryLowerElementAddress(index, out var address, out var elementType, out _)) return false;

        value = _builder.BuildLoad2(elementType, address, "item");
        return true;
    }

    // Anything that can be assigned to or stepped: a local, a parameter or an array element
    private bool TryGetAddress(ExpressionNode target, out LLVMValueRef address, out LLVMTypeRef type, out AeroType aeroType)
    {
        address = default;
        type = default;
        aeroType = AeroType.Error;

        while (target is ScopedExpressionNode s) target = s.Scoped;

        if (TryGetLocalSlot(target, out var variable, out var slot))
        {
            address = slot.Address;
            type = slot.Type;
            aeroType = variable.Type;
            return true;
        }

        if (target is IndexExpressionNode index) return TryLowerElementAddress(index, out address, out type, out aeroType);

        Fail("Only locals, parameters and array elements can be assigned so far", target.Span);
        return false;
    }
}
