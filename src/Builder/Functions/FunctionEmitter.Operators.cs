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
    private static bool IsDouble(AeroType? t) => IsKind(t, AeroType.Double);
    private static bool IsLong(AeroType? t) => IsKind(t, AeroType.Long);
    private static bool IsFloating(AeroType? t) => IsFloat(t) || IsDouble(t);
    private static bool IsByte(AeroType? t) => IsKind(t, AeroType.Byte); // Byte is unsigned
    private static bool IsWhole(AeroType? t) => IsKind(t, AeroType.Int) || IsLong(t) || IsByte(t);
    private static bool IsNumeric(AeroType? t) => IsWhole(t) || IsFloating(t);

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

        if (_typed.BindingOf(b) is OperatorBinding { Operator: var overload })
        {
            if (!TryLowerOperatorCall(overload, b.Left, b.Right, b.Span, out value)) return false;

            // '!=' that borrowed the 'Equality' overload negates its Bool (i8) result
            if (b.Operator == Operator.Inequality && overload.Op == Operator.Equality)
                value = _builder.BuildXor(value, LLVMValueRef.CreateConstInt(_context.Int8Type, 1), "ne");
            return true;
        }

        if (!TryLowerValue(b.Left, out var left) || !TryLowerValue(b.Right, out var right)) return false;
        return TryEmitBinary(b.Operator, left, TypeOf(b.Left), right, TypeOf(b.Right), b.Span, out value);
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
                if (!IsWhole(leftType))
                {
                    Fail($"'{op.AsString()}' is only lowered for Int, Long and Byte", span);
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

    // The floating type two numeric operands meet in: Double when either is a Double or a Long (Float can't hold a Long), else Float
    private LLVMTypeRef FloatingTypeOf(AeroType left, AeroType right)
        => IsDouble(left) || IsDouble(right) || IsLong(left) || IsLong(right) ? _context.DoubleType : _context.FloatType;

    // Whole numbers and Float widen to the target floating type; Int / Int is true division and also ends up as Float
    private LLVMValueRef ToFloating(LLVMValueRef value, AeroType type, LLVMTypeRef target)
    {
        if (IsFloating(type))
            return value.TypeOf.Kind == target.Kind ? value : _builder.BuildFPExt(value, target, "tof");
        return IsByte(type)
            ? _builder.BuildUIToFP(value, target, "tof")
            : _builder.BuildSIToFP(value, target, "tof");
    }

    // Int and Byte widen to Long when a Long is involved
    private LLVMValueRef ToWhole(LLVMValueRef value, AeroType type, bool wide)
    {
        if (!wide || IsLong(type)) return value;
        return IsByte(type)
            ? _builder.BuildZExt(value, _context.Int64Type, "wide")
            : _builder.BuildSExt(value, _context.Int64Type, "wide");
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
        var longDivide = op == Operator.Divide && (IsLong(leftType) || IsLong(rightType)) && !IsByte(leftType) && !IsByte(rightType);
        var asFloat = IsFloating(leftType) || IsFloating(rightType) || (op == Operator.Divide && bothInt) || longDivide;

        if (asFloat)
        {
            var floating = FloatingTypeOf(leftType, rightType);
            var l = ToFloating(left, leftType, floating);
            var r = ToFloating(right, rightType, floating);
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

        // Mixed Int and Byte has no promotion in the checker, so apart from a Long both sides share one type here
        var wide = IsLong(leftType) || IsLong(rightType);
        left = ToWhole(left, leftType, wide);
        right = ToWhole(right, rightType, wide);

        var unsigned = IsByte(leftType) && IsByte(rightType);
        value = op switch
        {
            Operator.Add => _builder.BuildAdd(left, right, "add"),
            Operator.Subtract => _builder.BuildSub(left, right, "sub"),
            Operator.Multiply => _builder.BuildMul(left, right, "mul"),
            Operator.Divide => unsigned ? _builder.BuildUDiv(left, right, "div") : _builder.BuildSDiv(left, right, "div"), // Long / Byte gets here
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

        if (op is Operator.Equality or Operator.Inequality && leftType == rightType && _types.SymbolOf(leftType, _scope) is RecordSymbol record)
            return TryEmitRecordEquality(op, record, left, right, span, out value);

        if (op is Operator.Equality or Operator.Inequality && IsKind(leftType, AeroType.String) && IsKind(rightType, AeroType.String))
        {
            value = BuildStringEquality(op, left, right);
            return true;
        }

        LLVMValueRef test;

        if (IsFloating(leftType) || IsFloating(rightType))
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
            var floating = FloatingTypeOf(leftType, rightType);
            test = _builder.BuildFCmp(predicate, ToFloating(left, leftType, floating), ToFloating(right, rightType, floating), "cmp");
        }
        else
        {
            var isEquality = op is Operator.Equality or Operator.Inequality;
            var integral = IsWhole(leftType) && IsWhole(rightType);
            var isEnum = leftType is ScalarType && _types.SymbolOf(leftType, _scope) is EnumSymbol; // an enum value is its ordinal
            var comparable = integral || (isEquality && (IsKind(leftType, AeroType.Bool) || IsKind(leftType, AeroType.Char) || isEnum));

            if (!comparable || (leftType.Name != rightType.Name && !integral))
            {
                Fail($"'{op.AsString()}' is not lowered for '{leftType}' and '{rightType}' yet", span);
                return false;
            }

            var wide = IsLong(leftType) || IsLong(rightType);
            left = ToWhole(left, leftType, wide);
            right = ToWhole(right, rightType, wide);

            var unsigned = IsByte(leftType) && IsByte(rightType);
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

        if (_typed.BindingOf(u) is OperatorBinding { Operator: var overload })
        {
            return u.Operator is Operator.Increment or Operator.Decrement
                ? TryLowerStepOverload(u, overload, out value)
                : TryLowerOperatorCall(overload, u.Operand, null, u.Span, out value);
        }

        if (u.Operator is Operator.Increment or Operator.Decrement) return TryLowerStep(u, out value);

        if (!TryLowerValue(u.Operand, out var operand)) return false;
        var type = TypeOf(u.Operand);

        switch (u.Operator)
        {
            case Operator.Add when IsNumeric(type):
                value = operand;
                return true;
            case Operator.Subtract when IsFloating(type):
                value = _builder.BuildFNeg(operand, "neg");
                return true;
            case Operator.Subtract when IsNumeric(type):
                value = _builder.BuildNeg(operand, "neg");
                return true;
            case Operator.LogicalNot or Operator.Not when IsKind(type, AeroType.Bool):
                value = _builder.BuildXor(operand, LLVMValueRef.CreateConstInt(_context.Int8Type, 1), "not");
                return true;
            case Operator.Tilde when IsWhole(type):
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

        if (!TryGetAddress(u.Operand, out var address, out var slotType, out _)) return false;
        var slot = new LocalSlot(address, slotType);

        var type = TypeOf(u.Operand);
        if (!IsNumeric(type))
        {
            Fail($"'{u.Operator.AsString()}' is not lowered for '{type}'", u.Span);
            return false;
        }

        var old = _builder.BuildLoad2(slot.Type, slot.Address, "old");
        var increment = u.Operator == Operator.Increment;
        LLVMValueRef updated;

        if (IsFloating(type))
        {
            var one = LLVMValueRef.CreateConstReal(slot.Type, 1.0);
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

    private bool TryGetLocalSlot(ExpressionNode target, out  VariableSymbol variable, out LocalSlot slot)
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

        if (_typed.BindingOf(a) is OperatorBinding { Operator: var overload })
        {
            LowerOverloadedCompound(a, overload);
            return;
        }

        if (IsPropertyAccess(a.Target))
        {
            LowerPropertyCompound(a, plain);
            return;
        }

        if (!TryGetAddress(a.Target, out var address, out var slotType, out var targetType)) return;
        var slot = new LocalSlot(address, slotType);

        if (!TryLowerValue(a.Value, out var right)) return;

        var current = _builder.BuildLoad2(slot.Type, slot.Address, "cur");
        if (!TryEmitBinary(plain, current, targetType, right, TypeOf(a.Value), a.Span, out var result)) return;

        _builder.BuildStore(result, slot.Address);
    }
}