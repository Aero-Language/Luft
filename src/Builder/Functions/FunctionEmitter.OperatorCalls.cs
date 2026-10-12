using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

// An overloaded operator is a method: self is the left operand, the parameter the right one, no parameter means unary
public sealed partial class FunctionEmitter
{
    public void EmitOperator(OperatorSymbol symbol, DeclaredFunction declared)
        => EmitBody(declared, symbol.Declaration.Parameters, symbol.Body, symbol.ReturnType);

    private bool TryGetOperator(OperatorSymbol op, SourceSpan span, out DeclaredFunction declared)
    {
        if (_members.Operators.TryGetValue(op, out var found))
        {
            declared = found;
            return true;
        }

        declared = null!;
        Fail($"The operator '{op.Op.AsString()}' cannot be called yet, only non generic types can declare operators", span);
        return false;
    }

    // left op right, or op left when right is null
    private bool TryLowerOperatorCall(OperatorSymbol op, ExpressionNode left, ExpressionNode? right, SourceSpan span, out LLVMValueRef value)
    {
        value = default;
        if (!TryGetOperator(op, span, out var declared)) return false;
        if (!TryLowerObject(left, declared.Owner!, out var receiver)) return false;

        return TryCallOperator(op, declared, receiver, right, span, out value);
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
            arguments.Add(Coerce(argument, _typed.TypeOf(right), parameter.Type, right.Span));
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
        if (!TryGetOperator(op, u.Span, out var declared)) return false;
        if (!TryGetAddress(u.Operand, out var address, out var slotType, out _)) return false;

        var old = _builder.BuildLoad2(slotType, address, "old");

        // A class operand is its pointer, a struct operand passes its own address
        var receiver = declared.Owner is ClassSymbol ? old : address;
        if (!TryCallOperator(op, declared, receiver, null, u.Span, out var result)) return false;

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
        if (!TryGetOperator(op, a.Span, out var declared)) return;

        if (IsPropertyAccess(a.Target))
        {
            if (!TryLowerProperty(a.Target, out var current)) return;

            LLVMValueRef receiver;
            if (declared.Owner is ClassSymbol) receiver = current;
            else
            {
                receiver = EntryAlloca(current.TypeOf, "temp");
                _builder.BuildStore(current, receiver);
            }

            if (!TryCallOperator(op, declared, receiver, a.Value, a.Span, out var computed)) return;
            TryStoreProperty(a.Target, computed, op.ReturnType, a.Span);
            return;
        }

        if (!TryGetAddress(a.Target, out var address, out var slotType, out var targetType)) return;

        var self = declared.Owner is ClassSymbol ? _builder.BuildLoad2(slotType, address, "cur") : address;
        if (!TryCallOperator(op, declared, self, a.Value, a.Span, out var result)) return;

        if (result.Handle == IntPtr.Zero)
        {
            Fail($"The operator '{op.Op.AsString()}' has to return a value to be used with an assignment", a.Span);
            return;
        }

        _builder.BuildStore(Coerce(result, op.ReturnType, targetType, a.Span), address);
    }
}
