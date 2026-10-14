using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

// Instance methods take 'self' as a pointer first: the object for classes, the value's address for structs and records
public sealed partial class FunctionEmitter
{
    // The object's first slot points at its vtable, whose entries are the destroy function followed by the virtual methods
    private LLVMValueRef LoadVirtualFunction(LLVMValueRef receiver, int slot)
    {
        var table = _builder.BuildLoad2(_types.Ptr, receiver, "vtable");
        var entry = _builder.BuildGEP2(_types.Ptr, table, new[] { LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)slot) }, "slot");
        return _builder.BuildLoad2(_types.Ptr, entry, "virtual");
    }

    // 'self' as a value: the object pointer for a class, a copy of the value for a struct
    private bool TryLowerSelf(SourceSpan span, out LLVMValueRef value)
    {
        value = _self;
        if (_selfOwner is ClassSymbol) return true;

        // An extension on a primitive, a string or an array gets the receiver by address
        if (_selfReceiver is not null && _selfOwner is null)
        {
            if (!_types.TryLower(_selfReceiver, _scope, span, out var receiverType)) return false;

            value = _builder.BuildLoad2(receiverType, _self, "self");
            return true;
        }

        var layout = _types.LayoutOf(_selfOwner!, span);
        if (layout is null) return false;

        value = _builder.BuildLoad2(layout.StructType, _self, "self");
        return true;
    }

    // `base.Method()` names the inherited implementation, so it never goes through the vtable
    private static bool IsBaseCall(CallExpressionNode call)
    {
        var target = call.Target;
        while (target is ScopedExpressionNode s) target = s.Scoped;

        return target is MemberAccessExpressionNode { Target: LiteralExpressionNode { LiteralType: TokenType.BaseLiteral } };
    }

    // The self argument of a call: `obj.Method()` uses obj, a bare `Method()` inside a method reuses the current self
    private bool TryLowerReceiver(CallExpressionNode call, TypeSymbol owner, out LLVMValueRef receiver)
    {
        receiver = default;

        var target = call.Target;
        while (target is ScopedExpressionNode s) target = s.Scoped;

        switch (target)
        {
            case MemberAccessExpressionNode m:
                return TryLowerObject(m.Target, owner, out receiver);
            case IdentifierExpressionNode when _self.Handle != IntPtr.Zero:
                receiver = _self;
                return true;
            default:
                Fail("An instance method needs an object to be called on", call.Span);
                return false;
        }
    }

    // A class is already a pointer, a struct needs an address: its own if it has one, otherwise a temporary copy
    private bool TryLowerObject(ExpressionNode expression, TypeSymbol owner, out LLVMValueRef pointer)
    {
        pointer = default;

        while (expression is ScopedExpressionNode s) expression = s.Scoped;

        if (expression is LiteralExpressionNode { LiteralType: TokenType.SelfLiteral or TokenType.BaseLiteral } && _self.Handle != IntPtr.Zero)
        {
            pointer = _self;
            return true;
        }

        if (owner is ClassSymbol) return TryLowerValue(expression, out pointer);
        if (IsAddressable(expression)) return TryGetAddress(expression, out pointer, out _, out _);

        if (!TryLowerValue(expression, out var value)) return false;

        pointer = EntryAlloca(value.TypeOf, "temp");
        _builder.BuildStore(value, pointer);
        return true;
    }

    // True for what has a memory location: locals, array elements, self and instance fields reached through one of those
    private bool IsAddressable(ExpressionNode expression)
    {
        expression = StripScopes(expression);

        switch (expression)
        {
            case IndexExpressionNode:
                return true;
            case LiteralExpressionNode { LiteralType: TokenType.SelfLiteral or TokenType.BaseLiteral }:
                return _self.Handle != IntPtr.Zero;
            case IdentifierExpressionNode when _typed.BindingOf(expression) is LocalBinding { Variable: var variable }:
                return _locals.ContainsKey(variable);
            case IdentifierExpressionNode when _typed.BindingOf(expression) is FieldBinding { Field: var named }:
                return _members.Fields.ContainsKey(named) || _self.Handle != IntPtr.Zero;
            case MemberAccessExpressionNode member when _typed.BindingOf(member) is FieldBinding { Field: var accessed } && _members.Fields.ContainsKey(accessed):
                return true;
            case MemberAccessExpressionNode member when _typed.BindingOf(member) is FieldBinding:
            {
                var targetType = _typed.TypeOf(member.Target);
                var owner = targetType is null ? null : _types.SymbolOf(targetType, _scope);
                return owner is ClassSymbol || IsAddressable(member.Target);
            }
            default:
                return false;
        }
    }
}
