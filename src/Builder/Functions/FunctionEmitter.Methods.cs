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
    // 'self' as a value: the object pointer for a class, a copy of the value for a struct
    private bool TryLowerSelf(SourceSpan span, out LLVMValueRef value)
    {
        value = _self;
        if (_selfOwner is ClassSymbol) return true;

        var layout = _types.LayoutOf(_selfOwner!, span);
        if (layout is null) return false;

        value = _builder.BuildLoad2(layout.StructType, _self, "self");
        return true;
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

        if (expression is LiteralExpressionNode { LiteralType: TokenType.SelfLiteral } && _self.Handle != IntPtr.Zero)
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

    // True where TryGetAddress succeeds without reporting an error
    private bool IsAddressable(ExpressionNode expression)
    {
        while (expression is ScopedExpressionNode s) expression = s.Scoped;

        return expression is IndexExpressionNode
            || (expression is IdentifierExpressionNode
                && _typed.BindingOf(expression) is LocalBinding { Variable: var variable }
                && _locals.ContainsKey(variable));
    }
}
