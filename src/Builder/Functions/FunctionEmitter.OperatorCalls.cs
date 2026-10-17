using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

// An instance overload is a method: self is the left operand, the parameter the right one, no parameter means unary.
// A static overload takes every operand as a parameter and has no self
public sealed partial class FunctionEmitter
{
    private readonly record struct Operand(LLVMValueRef Value, AeroType Type);

    public void EmitOperator(OperatorSymbol symbol, DeclaredFunction declared)
        => WithInstance(declared.Owner, null, null, () => EmitBody(declared, symbol.Declaration.Parameters, symbol.Body, Subst(symbol.ReturnType)));

    // An operator of a generic type is looked up in the instance of the operand it was found through
    private bool TryGetOperator(ref OperatorSymbol op, ExpressionNode operand, SourceSpan span, out DeclaredFunction declared)
    {
        if (_types.DefinitionOfMember(op) is { } definition && FindInstance(definition, operand, TypeOf(operand)) is { } owner)
            op = _types.MapMember(owner, op);

        if (_members.Operators.TryGetValue(op, out var found))
        {
            declared = found;
            return true;
        }

        declared = null!;
        Fail($"The operator '{op.Op.AsString()}' cannot be called, it was not declared", span);
        return false;
    }

    // left op right, or op left when right is null
    private bool TryLowerOperatorCall(OperatorSymbol op, ExpressionNode left, ExpressionNode? right, SourceSpan span, out LLVMValueRef value)
    {
        value = default;
        if (!TryGetOperator(ref op, left, span, out var declared)) return false;

        if (op.IsStatic)
        {
            var operands = new List<Operand>();
            var expressions = new List<ExpressionNode> { left };
            if (right is not null) expressions.Add(right);

            foreach (var expression in expressions)
            {
                if (!TryLowerValue(expression, out var lowered)) return false;
                operands.Add(new Operand(lowered, TypeOf(expression)));
            }

            return TryCallStatic(op, declared, operands, span, out value);
        }

        if (!TryLowerObject(left, declared.Owner!, out var receiver)) return false;

        return TryCallOperator(op, declared, receiver, right, span, out value);
    }

    private bool TryCallStatic(OperatorSymbol op, DeclaredFunction declared, List<Operand> operands, SourceSpan span, out LLVMValueRef value)
    {
        value = default;
        var parameters = op.Parameters;
        var arguments = new LLVMValueRef[operands.Count];

        for (int i = 0; i < operands.Count; i++)
        {
            if (parameters[i].Type.IsRef)
            {
                Fail($"The parameter of the operator '{op.Op.AsString()}' cannot be 'ref'", span);
                return false;
            }

            arguments[i] = Coerce(operands[i].Value, operands[i].Type, InOwner(parameters[i].Type, declared), span);
        }

        var isVoid = op.ReturnType == AeroType.Void;
        var result = _builder.BuildCall2(declared.Type, declared.Value, arguments, isVoid ? "" : "op");
        if (!isVoid) value = result;
        return true;
    }

    private bool TryCallOperator(OperatorSymbol op, DeclaredFunction declared, LLVMValueRef receiver, ExpressionNode? right, SourceSpan span, out LLVMValueRef value)
    {
        value = default;
        var arguments = new List<LLVMValueRef> { receiver };

        if (right is not null)
        {
            if (op.Parameters.Count != 1)
            {
                Fail($"The operator '{op.Op.AsString()}' does not take a right operand", span);
                return false;
            }

            var parameter = op.Parameters[0];
            if (parameter.Type.IsRef)
            {
                Fail($"The parameter of the operator '{op.Op.AsString()}' cannot be 'ref'", span);
                return false;
            }

            if (!TryLowerValue(right, out var argument)) return false;
            arguments.Add(Coerce(argument, TypeOf(right), InOwner(parameter.Type, declared), right.Span));
        }

        var isVoid = op.ReturnType == AeroType.Void;
        var result = _builder.BuildCall2(declared.Type, declared.Value, arguments.ToArray(), isVoid ? "" : "op");
        if (!isVoid) value = result;
        return true;
    }

    // ++ and -- through an overload: the operator gets the operand and its result is stored back, one returning Void changes the operand in place
    private bool TryLowerStepOverload(UnaryExpressionNode u, OperatorSymbol op, out LLVMValueRef value)
    {
        value = default;
        if (!TryGetOperator(ref op, u.Operand, u.Span, out var declared)) return false;
        if (!TryGetAddress(u.Operand, out var address, out var slotType, out _)) return false;

        var old = _builder.BuildLoad2(slotType, address, "old");

        LLVMValueRef result;
        if (op.IsStatic)
        {
            // The operand is passed by value, so the new value has to come back as the result
            if (op.ReturnType == AeroType.Void)
            {
                Fail($"The static operator '{op.Op.AsString()}' has to return the new value to be used with '++' or '--'", u.Span);
                return false;
            }

            if (!TryCallStatic(op, declared, [new Operand(old, TypeOf(u.Operand))], u.Span, out result)) return false;
        }
        else
        {
            // A class operand is its pointer, a struct operand passes its own address
            var receiver = declared.Owner is ClassSymbol ? old : address;
            if (!TryCallOperator(op, declared, receiver, null, u.Span, out result)) return false;
        }

        LLVMValueRef updated;
        if (op.ReturnType == AeroType.Void) updated = _builder.BuildLoad2(slotType, address, "new");
        else
        {
            _builder.BuildStore(result, address);
            updated = result;
        }

        value = u.IsPostFix ? old : updated;
        return true;
    }

    // target op= value as target = target op value, the result of the operator is written back
    private void LowerOverloadedCompound(AssignmentStatementNode a, OperatorSymbol op)
    {
        if (!TryGetOperator(ref op, a.Target, a.Span, out var declared)) return;

        if (IsPropertyAccess(a.Target))
        {
            if (!TryLowerProperty(a.Target, out var current)) return;

            LLVMValueRef computed;
            if (op.IsStatic)
            {
                if (!TryCallStaticWith(op, declared, current, TypeOf(a.Target), a.Value, a.Span, out computed)) return;
            }
            else
            {
                LLVMValueRef receiver;
                if (declared.Owner is ClassSymbol) receiver = current;
                else
                {
                    receiver = EntryAlloca(current.TypeOf, "temp");
                    _builder.BuildStore(current, receiver);
                }

                if (!TryCallOperator(op, declared, receiver, a.Value, a.Span, out computed)) return;
            }

            TryStoreProperty(a.Target, computed, op.ReturnType, a.Span);
            return;
        }

        if (!TryGetAddress(a.Target, out var address, out var slotType, out var targetType)) return;

        LLVMValueRef result;
        if (op.IsStatic)
        {
            var currentValue = _builder.BuildLoad2(slotType, address, "cur");
            if (!TryCallStaticWith(op, declared, currentValue, targetType, a.Value, a.Span, out result)) return;
        }
        else
        {
            var self = declared.Owner is ClassSymbol ? _builder.BuildLoad2(slotType, address, "cur") : address;
            if (!TryCallOperator(op, declared, self, a.Value, a.Span, out result)) return;
        }

        if (result.Handle == IntPtr.Zero)
        {
            Fail($"The operator '{op.Op.AsString()}' has to return a value to be used with an assignment", a.Span);
            return;
        }

        _builder.BuildStore(Coerce(result, op.ReturnType, targetType, a.Span), address);
    }

    // A static operator called with an operand that is already lowered and a right operand still to lower
    private bool TryCallStaticWith(OperatorSymbol op, DeclaredFunction declared, LLVMValueRef left, AeroType leftType, ExpressionNode right, SourceSpan span, out LLVMValueRef value)
    {
        value = default;
        if (!TryLowerValue(right, out var rightValue)) return false;

        return TryCallStatic(op, declared, [new Operand(left, leftType), new Operand(rightValue, TypeOf(right))], span, out value);
    }
}
