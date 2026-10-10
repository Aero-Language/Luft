using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.Utility;

namespace Luft.Builder;

// String conversion, concatenation and equality, all through the C runtime (Runtime/aero_runtime.c)
public sealed partial class FunctionEmitter
{
    // Strings cross the C boundary by pointer, so no struct ABI has to match
    private LLVMValueRef CallRuntime(string name, LLVMTypeRef returnType, params LLVMValueRef[] arguments)
    {
        var type = LLVMTypeRef.CreateFunction(returnType, arguments.Select(a => a.TypeOf).ToArray());

        var function = _module.GetNamedFunction(name);
        if (function.Handle == IntPtr.Zero) function = _module.AddFunction(name, type);

        var isVoid = returnType.Kind == LLVMTypeKind.LLVMVoidTypeKind;
        return _builder.BuildCall2(type, function, arguments, isVoid ? "" : "rt");
    }

    private LLVMValueRef SpillString(LLVMValueRef value)
    {
        var slot = EntryAlloca(_types.StringStruct, "str.tmp");
        _builder.BuildStore(value, slot);
        return slot;
    }

    // out-parameter helper: the runtime fills a String slot, which is then loaded
    private LLVMValueRef BuildString(string name, params LLVMValueRef[] arguments)
    {
        var slot = EntryAlloca(_types.StringStruct, "str.out");
        CallRuntime(name, _context.VoidType, arguments.Prepend(slot).ToArray());
        return _builder.BuildLoad2(_types.StringStruct, slot, "str");
    }

    private bool TryToString(LLVMValueRef value, AeroType type, SourceSpan span, out LLVMValueRef text)
    {
        text = default;

        if (type.IsNullable)
        {
            Fail("Nullable values cannot be turned into a String yet", span);
            return false;
        }

        if (IsKind(type, AeroType.String)) text = value;
        else if (IsKind(type, AeroType.Int)) text = BuildString("aero_str_from_int", value);
        else if (IsByte(type)) text = BuildString("aero_str_from_int", _builder.BuildZExt(value, _context.Int32Type, "wide"));
        else if (IsFloat(type)) text = BuildString("aero_str_from_float", value);
        else if (IsKind(type, AeroType.Bool)) text = BuildString("aero_str_from_bool", value);
        else if (IsKind(type, AeroType.Char)) text = BuildString("aero_str_from_char", value);
        else
        {
            Fail($"'{type}' cannot be turned into a String yet", span);
            return false;
        }
        return true;
    }

    private bool TryLowerInterpolation(StringInterpolationExpressionNode node, out LLVMValueRef value)
    {
        value = default;

        var hasResult = false;
        LLVMValueRef result = default;

        foreach (var part in node.Parts)
        {
            if (!TryLowerValue(part, out var lowered)) return false;

            var type = _typed.TypeOf(part);
            if (type is null)
            {
                Fail("The interpolated expression was not resolved", part.Span);
                return false;
            }
            if (!TryToString(lowered, type, part.Span, out var text)) return false;

            result = hasResult
                ? BuildString("aero_str_concat", SpillString(result), SpillString(text))
                : text;
            hasResult = true;
        }

        value = hasResult ? result : StringConstant("");
        return true;
    }

    private LLVMValueRef BuildStringEquality(Operator op, LLVMValueRef left, LLVMValueRef right)
    {
        var equal = CallRuntime("aero_str_eq", _context.Int8Type, SpillString(left), SpillString(right));
        return op == Operator.Equality ? equal : _builder.BuildXor(equal, LLVMValueRef.CreateConstInt(_context.Int8Type, 1), "ne");
    }
}
