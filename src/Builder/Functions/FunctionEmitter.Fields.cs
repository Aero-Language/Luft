using LLVMSharp.Interop;
using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

// Instance fields sit in the object (class) or in the value (struct), both are reached through a pointer plus the layout slot
public sealed partial class FunctionEmitter
{
    private static ExpressionNode StripScopes(ExpressionNode expression)
    {
        while (expression is ScopedExpressionNode s) expression = s.Scoped;
        return expression;
    }

    private static bool IsStaticField(FieldSymbol field)
        => field.Declaration.MemberMods.HasFlag(MemberMod.Static) || field.VarKind == VariableKind.Const;

    private bool IsFieldAccess(ExpressionNode expression)
    {
        expression = StripScopes(expression);
        return expression is IdentifierExpressionNode or MemberAccessExpressionNode && _typed.BindingOf(expression) is FieldBinding;
    }

    // A bare name starts from self, `obj.name` from the object or struct on its left
    private bool TryGetFieldAddress(ExpressionNode expression, bool forWrite, out LLVMValueRef address, out LLVMTypeRef type, out AeroType aeroType)
    {
        address = default;
        type = default;
        aeroType = AeroType.Error;

        expression = StripScopes(expression);
        if (_typed.BindingOf(expression) is not FieldBinding { Field: var field })
        {
            Fail("This is not a field", expression.Span);
            return false;
        }

        if (IsStaticField(field))
        {
            Fail($"The static field '{field.Name}' is lowered in sub-task 6d", expression.Span);
            return false;
        }

        TypeSymbol? owner;
        LLVMValueRef pointer;

        if (expression is MemberAccessExpressionNode member)
        {
            var targetType = _typed.TypeOf(member.Target);
            owner = targetType is null ? null : _types.SymbolOf(targetType, _scope);
            if (owner is null)
            {
                Fail($"The type of the object holding '{field.Name}' is unknown", expression.Span);
                return false;
            }

            if (forWrite && owner is not ClassSymbol && !IsAddressable(member.Target))
            {
                Fail($"The field '{field.Name}' cannot be assigned on a temporary value", expression.Span);
                return false;
            }

            if (!TryLowerObject(member.Target, owner, out pointer)) return false;
        }
        else
        {
            if (_self.Handle == IntPtr.Zero || _selfOwner is null)
            {
                Fail($"The field '{field.Name}' needs an object", expression.Span);
                return false;
            }

            owner = _selfOwner;
            pointer = _self;
        }

        var layout = _types.LayoutOf(owner, expression.Span);
        if (layout is null) return false;

        if (layout.Find(field.Name) is not { } slot)
        {
            Fail($"'{field.Name}' is not a field of '{owner.Name}'", expression.Span);
            return false;
        }

        address = _builder.BuildStructGEP2(layout.StructType, pointer, (uint)slot.Index, field.Name);
        type = layout.Elements[slot.Index];
        aeroType = slot.Type;
        return true;
    }

    private bool TryLowerField(ExpressionNode expression, out LLVMValueRef value)
    {
        value = default;
        if (!TryGetFieldAddress(expression, false, out var address, out var type, out _)) return false;

        value = _builder.BuildLoad2(type, address, "field");
        return true;
    }
}
