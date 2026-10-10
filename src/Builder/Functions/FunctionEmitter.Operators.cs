using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

// Primitive operators: arithmetic, comparison, bitwise, unary and compound assignment
public sealed partial class FunctionEmitter
{
    private static bool IsKind(AeroType? type, AeroType kind)
        => type is ScalarType { IsNullable: false } && type.Name == kind.Name;

    private static bool IsFloat(AeroType? t) => IsKind(t, AeroType.Float);
    private static bool IsByte(AeroType? t) => IsKind(t, AeroType.Byte); // Byte is unsigned
    private static bool IsNumeric(AeroType? t) => IsKind(t, AeroType.Int) || IsFloat(t) || IsByte(t);

    private bool TryLowerBinary(BinaryExpressionNode b, out LLVMValueRef value)
    {
        value = default;

        switch (b.Operator)
        {
            case Operator.CastSymbol:
                return TryLowerCast(b, out value);
            case Operator.And or Operator.Or or Operator.LogicalAnd or Operator.LogicalOr:
                return TryLowerShortCircuit(b, out value);
        }

        if (_typed.BindingOf(b) is OperatorBinding)
        {
            Fail("Overloaded operators are lowered in sub-task 5f", b.Span);
            return false;
        }

        if (!TryLowerValue(b.Left, out var left) || !TryLowerValue(b.Right, out var right)) return false;
        return TryEmitBinary(b.Operator, left, _typed.TypeOf(b.Left), right, _typed.TypeOf(b.Right), b.Span, out value);
    }

    private bool TryEmitBinary(Operator op, LLVMValueRef left, AeroType? leftType, LLVMValueRef right, AeroType? rightType,
        SourceSpan span, out LLVMValueRef value)
    {
        value = default;

        if (leftType is null || rightType is null)
        {
            Fail("The operand types were not resolved", span);
            return false;
        }

        switch (op)
        {
            case Operator.Add or Operator.Subtract or Operator.Multiply or Operator.Divide or Operator.Modulo:
                return TryEmitArithmetic(op, left, leftType, right, rightType, span, out value);

            case Operator.Equality or Operator.Inequality
                or Operator.LessThan or Operator.GreaterThan or Operator.LessThanEqual or Operator.GreaterThanEqual:
                return TryEmitComparison(op, left, leftType, right, rightType, span, out value);

            case Operator.LogicalXor when IsKind(leftType, AeroType.Bool):
                value = _builder.BuildXor(left, right, "xor");
                return true;

            case Operator.BitwiseAnd or Operator.BitwiseOr or Operator.BitwiseXor or Operator.LeftShift or Operator.RightShift:
                if (!IsKind(leftType, AeroType.Int) && !IsByte(leftType))
                {
                    Fail($"'{op.AsString()}' is only lowered for Int and Byte", span);
                    return false;
                }

                value = op switch
                {
                    Operator.BitwiseAnd => _builder.BuildAnd(left, right, "and"),
                    Operator.BitwiseOr => _builder.BuildOr(left, right, "or"),
                    Operator.BitwiseXor => _builder.BuildXor(left, right, "xor"),
                    Operator.LeftShift => _builder.BuildShl(left, right, "shl"),
                    _ => IsByte(leftType) ? _builder.BuildLShr(left, right, "shr") : _builder.BuildAShr(left, right, "shr"),
                };
                return true;

            default:
                Fail($"The operator '{op.AsString()}' is not lowered yet", span);
                return false;
        }
    }

    // Int and Byte widen to Float when a Float is involved; Int / Int is true division and also ends up as Float
    private LLVMValueRef ToFloat(LLVMValueRef value, AeroType type)
    {
        if (IsFloat(type)) return value;
        return IsByte(type)
            ? _builder.BuildUIToFP(value, _context.FloatType, "tof")
            : _builder.BuildSIToFP(value, _context.FloatType, "tof");
    }

    private bool TryEmitArithmetic(Operator op, LLVMValueRef left, AeroType leftType, LLVMValueRef right, AeroType rightType,
        SourceSpan span, out LLVMValueRef value)
    {
        value = default;

        if (!IsNumeric(leftType) || !IsNumeric(rightType))
        {
            Fail($"'{op.AsString()}' is not lowered for '{leftType}' and '{rightType}' yet", span);
            return false;
        }

        var bothInt = IsKind(leftType, AeroType.Int) && IsKind(rightType, AeroType.Int);
        var asFloat = IsFloat(leftType) || IsFloat(rightType) || (op == Operator.Divide && bothInt);

        if (asFloat)
        {
            var l = ToFloat(left, leftType);
            var r = ToFloat(right, rightType);
            value = op switch
            {
                Operator.Add => _builder.BuildFAdd(l, r, "add"),
                Operator.Subtract => _builder.BuildFSub(l, r, "sub"),
                Operator.Multiply => _builder.BuildFMul(l, r, "mul"),
                Operator.Divide => _builder.BuildFDiv(l, r, "div"),
                _ => _builder.BuildFRem(l, r, "rem"),
            };
            return true;
        }

        // Mixed Int and Byte has no promotion in the checker, so both sides share one type here
        var unsigned = IsByte(leftType);
        value = op switch
        {
            Operator.Add => _builder.BuildAdd(left, right, "add"),
            Operator.Subtract => _builder.BuildSub(left, right, "sub"),
            Operator.Multiply => _builder.BuildMul(left, right, "mul"),
            Operator.Divide => _builder.BuildUDiv(left, right, "div"), // only Byte / Byte gets here
            _ => unsigned ? _builder.BuildURem(left, right, "rem") : _builder.BuildSRem(left, right, "rem"),
        };
        return true;
    }

    private bool TryEmitComparison(Operator op, LLVMValueRef left, AeroType leftType, LLVMValueRef right, AeroType rightType,
        SourceSpan span, out LLVMValueRef value)
    {
        value = default;

        if (leftType.IsNullable || rightType.IsNullable || leftType == AeroType.Null || rightType == AeroType.Null)
        {
            Fail("Comparing nullable values is not lowered yet", span);
            return false;
        }

        if (op is Operator.Equality or Operator.Inequality && IsKind(leftType, AeroType.String) && IsKind(rightType, AeroType.String))
        {
            value = BuildStringEquality(op, left, right);
            return true;
        }

        LLVMValueRef test;

        if (IsFloat(leftType) || IsFloat(rightType))
        {
            if (!IsNumeric(leftType) || !IsNumeric(rightType))
            {
                Fail($"'{op.AsString()}' is not lowered for '{leftType}' and '{rightType}' yet", span);
                return false;
            }

            var predicate = op switch
            {
                Operator.Equality => LLVMRealPredicate.LLVMRealOEQ,
                Operator.Inequality => LLVMRealPredicate.LLVMRealUNE,
                Operator.LessThan => LLVMRealPredicate.LLVMRealOLT,
                Operator.GreaterThan => LLVMRealPredicate.LLVMRealOGT,
                Operator.LessThanEqual => LLVMRealPredicate.LLVMRealOLE,
                _ => LLVMRealPredicate.LLVMRealOGE,
            };
            test = _builder.BuildFCmp(predicate, ToFloat(left, leftType), ToFloat(right, rightType), "cmp");
        }
        else
        {
            var isEquality = op is Operator.Equality or Operator.Inequality;
            var integral = IsKind(leftType, AeroType.Int) || IsByte(leftType);
            var comparable = integral || (isEquality && (IsKind(leftType, AeroType.Bool) || IsKind(leftType, AeroType.Char)));

            if (!comparable || leftType.Name != rightType.Name)
            {
                Fail($"'{op.AsString()}' is not lowered for '{leftType}' and '{rightType}' yet", span);
                return false;
            }

            var unsigned = IsByte(leftType);
            var predicate = op switch
            {
                Operator.Equality => LLVMIntPredicate.LLVMIntEQ,
                Operator.Inequality => LLVMIntPredicate.LLVMIntNE,
                Operator.LessThan => unsigned ? LLVMIntPredicate.LLVMIntULT : LLVMIntPredicate.LLVMIntSLT,
                Operator.GreaterThan => unsigned ? LLVMIntPredicate.LLVMIntUGT : LLVMIntPredicate.LLVMIntSGT,
                Operator.LessThanEqual => unsigned ? LLVMIntPredicate.LLVMIntULE : LLVMIntPredicate.LLVMIntSLE,
                _ => unsigned ? LLVMIntPredicate.LLVMIntUGE : LLVMIntPredicate.LLVMIntSGE,
            };
            test = _builder.BuildICmp(predicate, left, right, "cmp");
        }

        // Bool is i8 everywhere
        value = _builder.BuildZExt(test, _context.Int8Type, "bool");
        return true;
    }

    private bool TryLowerUnary(UnaryExpressionNode u, out LLVMValueRef value)
    {
        value = default;

        if (_typed.BindingOf(u) is OperatorBinding)
        {
            Fail("Overloaded operators are lowered in sub-task 5f", u.Span);
            return false;
        }

        if (u.Operator is Operator.Increment or Operator.Decrement) return TryLowerStep(u, out value);

        if (!TryLowerValue(u.Operand, out var operand)) return false;
        var type = _typed.TypeOf(u.Operand);

        switch (u.Operator)
        {
            case Operator.Add when IsNumeric(type):
                value = operand;
                return true;
            case Operator.Subtract when IsFloat(type):
                value = _builder.BuildFNeg(operand, "neg");
                return true;
            case Operator.Subtract when IsNumeric(type):
                value = _builder.BuildNeg(operand, "neg");
                return true;
            case Operator.LogicalNot or Operator.Not when IsKind(type, AeroType.Bool):
                value = _builder.BuildXor(operand, LLVMValueRef.CreateConstInt(_context.Int8Type, 1), "not");
                return true;
            case Operator.Tilde when IsKind(type, AeroType.Int) || IsByte(type):
                value = _builder.BuildNot(operand, "inv");
                return true;
            default:
                Fail($"The unary '{u.Operator.AsString()}' is not lowered for '{type}' yet", u.Span);
                return false;
        }
    }

    // ++ and -- on a local; the postfix form yields the old value
    private bool TryLowerStep(UnaryExpressionNode u, out LLVMValueRef value)
    {
        value = default;

        if (!TryGetLocalSlot(u.Operand, out _, out var slot))
        {
            Fail($"'{u.Operator.AsString()}' only works on locals and parameters so far", u.Span);
            return false;
        }

        var type = _typed.TypeOf(u.Operand);
        if (!IsNumeric(type))
        {
            Fail($"'{u.Operator.AsString()}' is not lowered for '{type}'", u.Span);
            return false;
        }

        var old = _builder.BuildLoad2(slot.Type, slot.Address, "old");
        var increment = u.Operator == Operator.Increment;
        LLVMValueRef updated;

        if (IsFloat(type))
        {
            var one = LLVMValueRef.CreateConstReal(_context.FloatType, 1.0);
            updated = increment ? _builder.BuildFAdd(old, one, "inc") : _builder.BuildFSub(old, one, "dec");
        }
        else
        {
            var one = LLVMValueRef.CreateConstInt(slot.Type, 1);
            updated = increment ? _builder.BuildAdd(old, one, "inc") : _builder.BuildSub(old, one, "dec");
        }

        _builder.BuildStore(updated, slot.Address);
        value = u.IsPostFix ? old : updated;
        return true;
    }

    private bool TryGetLocalSlot(ExpressionNode target, out VariableSymbol variable, out LocalSlot slot)
    {
        variable = null!;
        slot = default;

        while (target is ScopedExpressionNode s) target = s.Scoped;

        if (target is IdentifierExpressionNode id
            && _typed.BindingOf(id) is LocalBinding { Variable: var v }
            && _locals.TryGetValue(v, out slot))
        {
            variable = v;
            return true;
        }
        return false;
    }

    private static readonly Dictionary<Operator, Operator> CompoundToPlain = new()
    {
        [Operator.AddAssign] = Operator.Add,
        [Operator.SubtractAssign] = Operator.Subtract,
        [Operator.MultiplyAssign] = Operator.Multiply,
        [Operator.DivideAssign] = Operator.Divide,
        [Operator.ModuloAssign] = Operator.Modulo,
        [Operator.AndAssign] = Operator.BitwiseAnd,
        [Operator.OrAssign] = Operator.BitwiseOr,
        [Operator.XorAssign] = Operator.BitwiseXor,
        [Operator.LeftShiftAssign] = Operator.LeftShift,
        [Operator.RightShiftAssign] = Operator.RightShift,
    };

    // target op= value, lowered as target = target op value
    private void LowerCompoundAssignment(AssignmentStatementNode a)
    {
        if (!CompoundToPlain.TryGetValue(a.Operator, out var plain))
        {
            Fail($"'{a.Operator.AsString()}' is not a lowerable assignment operator", a.Span);
            return;
        }

        if (_typed.BindingOf(a) is OperatorBinding)
        {
            Fail("Overloaded operators are lowered in sub-task 5f", a.Span);
            return;
        }

        if (!TryGetLocalSlot(a.Target, out var variable, out var slot))
        {
            Fail("Only locals and parameters can be assigned so far", a.Target.Span);
            return;
        }

        if (!TryLowerValue(a.Value, out var right)) return;

        var current = _builder.BuildLoad2(slot.Type, slot.Address, variable.Name);
        if (!TryEmitBinary(plain, current, variable.Type, right, _typed.TypeOf(a.Value), a.Span, out var result)) return;

        _builder.BuildStore(result, slot.Address);
    }
}