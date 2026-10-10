using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.Utility;

namespace Luft.Builder;

// Type::value between the primitive types and to String
public sealed partial class FunctionEmitter
{
    private bool TryLowerCast(BinaryExpressionNode b, out LLVMValueRef value)
    {
        value = default;

        var target = _typed.TypeOf(b);
        var source = _typed.TypeOf(b.Right);
        if (target is null || source is null)
        {
            Fail("The cast was not resolved", b.Span);
            return false;
        }

        if (!TryLowerValue(b.Right, out var operand)) return false;
        return TryConvert(operand, source, target, b.Span, out value);
    }

    private static bool IsIntegral(AeroType t)
        => IsKind(t, AeroType.Int) || IsByte(t) || IsKind(t, AeroType.Char) || IsKind(t, AeroType.Bool);

    private bool TryConvert(LLVMValueRef operand, AeroType source, AeroType target, SourceSpan span, out LLVMValueRef value)
    {
        value = default;

        var primitive = (IsIntegral(source) || IsFloat(source)) && (IsIntegral(target) || IsFloat(target));

        if (source.Name == target.Name && !source.IsNullable && !target.IsNullable)
        {
            value = operand;
            return true;
        }

        if (IsKind(target, AeroType.String)) return TryToString(operand, source, span, out value);

        if (!primitive)
        {
            Fail($"Casting '{source}' to '{target}' is lowered together with user types (sub-task 6)", span);
            return false;
        }

        if (!_types.TryLower(target, _scope, span, out var to)) return false;

        if (IsFloat(source))
        {
            if (IsKind(target, AeroType.Bool))
            {
                Fail("A Float cannot be cast to Bool", span);
                return false;
            }
            value = IsKind(target, AeroType.Int) ? _builder.BuildFPToSI(operand, to, "cast") : _builder.BuildFPToUI(operand, to, "cast");
            return true;
        }

        if (IsFloat(target))
        {
            if (IsKind(source, AeroType.Bool))
            {
                Fail("A Bool cannot be cast to Float", span);
                return false;
            }
            value = IsKind(source, AeroType.Int) ? _builder.BuildSIToFP(operand, to, "cast") : _builder.BuildUIToFP(operand, to, "cast");
            return true;
        }

        if (IsKind(target, AeroType.Bool))
        {
            var test = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, operand, LLVMValueRef.CreateConstNull(operand.TypeOf), "nonzero");
            value = _builder.BuildZExt(test, to, "bool");
            return true;
        }

        var from = operand.TypeOf.IntWidth;
        var width = to.IntWidth;
        value = from < width ? _builder.BuildZExt(operand, to, "cast")
            : from > width ? _builder.BuildTrunc(operand, to, "cast")
            : operand;
        return true;
    }
}
