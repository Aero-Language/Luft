using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

// A property is stored in a backing value (a layout slot, or a global for static ones) and has accessor functions only where an accessor has a body.
// An accessor reads and writes that backing value as 'it'.
public sealed partial class FunctionEmitter
{
    private bool IsPropertyAccess(ExpressionNode expression)
    {
        expression = StripScopes(expression);
        return expression is IdentifierExpressionNode or MemberAccessExpressionNode && _typed.BindingOf(expression) is PropertyBinding;
    }

    private static bool HasAccessorBody(PropertySymbol property) => property.Getter?.Body is not null || property.Setter?.Body is not null;

    private bool IsGlobalProperty(PropertySymbol property) => _members.GlobalProperties.Contains(property);

    private bool TryGetPropertyBinding(ExpressionNode expression, out PropertySymbol property)
    {
        property = null!;
        if (_typed.BindingOf(expression) is not PropertyBinding binding)
        {
            Fail("This is not a property", expression.Span);
            return false;
        }

        property = binding.Property;
        return true;
    }

    // The object an instance property belongs to, nothing for a global one
    private bool TryPropertyReceiver(ExpressionNode expression, PropertySymbol property, bool forWrite, out LLVMValueRef pointer, out TypeSymbol? owner)
    {
        pointer = default;
        owner = null;

        if (IsGlobalProperty(property)) return true;

        if (expression is MemberAccessExpressionNode member)
        {
            var targetType = _typed.TypeOf(member.Target);
            owner = targetType is null ? null : _types.SymbolOf(targetType, _scope);
            if (owner is null)
            {
                Fail($"The type of the object holding '{property.Name}' is unknown", expression.Span);
                return false;
            }

            if (forWrite && owner is not ClassSymbol && !IsAddressable(member.Target))
            {
                Fail($"The property '{property.Name}' cannot be assigned on a temporary value", expression.Span);
                return false;
            }

            return TryLowerObject(member.Target, owner, out pointer);
        }

        if (_self.Handle == IntPtr.Zero || _selfOwner is null)
        {
            Fail($"The property '{property.Name}' needs an object", expression.Span);
            return false;
        }

        owner = _selfOwner;
        pointer = _self;
        return true;
    }

    private bool TryBackingAddress(PropertySymbol property, TypeSymbol? owner, LLVMValueRef pointer, SourceSpan span, out LLVMValueRef address, out LLVMTypeRef type)
    {
        address = default;
        type = default;

        if (IsGlobalProperty(property))
        {
            if (!_members.Backing.TryGetValue(property, out var global))
            {
                Fail($"The property '{property.Name}' has no stored value", span);
                return false;
            }

            address = global.Address;
            type = global.Type;
            return true;
        }

        var layout = owner is null ? null : _types.LayoutOf(owner, span);
        if (layout is null) return false;

        if (layout.Find($"<backing>{property.Name}") is not { } slot)
        {
            Fail($"The property '{property.Name}' has no stored value", span);
            return false;
        }

        address = _builder.BuildStructGEP2(layout.StructType, pointer, (uint)slot.Index, property.Name);
        type = layout.Elements[slot.Index];
        return true;
    }

    private bool TryLowerProperty(ExpressionNode expression, out LLVMValueRef value)
    {
        value = default;

        expression = StripScopes(expression);
        if (!TryGetPropertyBinding(expression, out var property)) return false;

        if (property.Getter is null && property.Setter is not null)
        {
            Fail($"The property '{property.Name}' has no getter", expression.Span);
            return false;
        }

        if (!TryPropertyReceiver(expression, property, false, out var pointer, out var owner)) return false;

        if (property.Getter?.Body is not null)
        {
            if (!_members.Accessors.TryGetValue(new AccessorKey(property, false), out var getter))
            {
                Fail($"The getter of '{property.Name}' cannot be called yet", expression.Span);
                return false;
            }

            var arguments = IsGlobalProperty(property) ? Array.Empty<LLVMValueRef>() : new[] { pointer };
            value = _builder.BuildCall2(getter.Type, getter.Value, arguments, "get");
            return true;
        }

        if (!TryBackingAddress(property, owner, pointer, expression.Span, out var address, out var type)) return false;

        value = _builder.BuildLoad2(type, address, property.Name);
        return true;
    }

    // Assigning calls the setter when it has a body and stores into the backing value otherwise
    private bool TryStoreProperty(ExpressionNode expression, LLVMValueRef value, AeroType? valueType, SourceSpan span)
    {
        expression = StripScopes(expression);
        if (!TryGetPropertyBinding(expression, out var property)) return false;

        if (property.Setter is null && property.Getter is not null)
        {
            Fail($"The property '{property.Name}' is read only", span);
            return false;
        }

        if (!TryPropertyReceiver(expression, property, true, out var pointer, out var owner)) return false;

        var coerced = Coerce(value, valueType, property.Type, span);

        if (property.Setter?.Body is not null)
        {
            if (!_members.Accessors.TryGetValue(new AccessorKey(property, true), out var setter))
            {
                Fail($"The setter of '{property.Name}' cannot be called yet", span);
                return false;
            }

            var arguments = IsGlobalProperty(property) ? new[] { coerced } : new[] { pointer, coerced };
            _builder.BuildCall2(setter.Type, setter.Value, arguments, "");
            return true;
        }

        if (!TryBackingAddress(property, owner, pointer, span, out var address, out _)) return false;

        _builder.BuildStore(coerced, address);
        return true;
    }

    // Only a property without accessor bodies has an address, the others are read and written through their accessors
    private bool TryGetPropertyAddress(ExpressionNode expression, out LLVMValueRef address, out LLVMTypeRef type, out AeroType aeroType)
    {
        address = default;
        type = default;
        aeroType = AeroType.Error;

        expression = StripScopes(expression);
        if (!TryGetPropertyBinding(expression, out var property)) return false;

        if (HasAccessorBody(property))
        {
            Fail($"The property '{property.Name}' has accessors, so it can only be assigned or compound assigned so far", expression.Span);
            return false;
        }

        if (!TryPropertyReceiver(expression, property, true, out var pointer, out var owner)) return false;

        aeroType = property.Type;
        return TryBackingAddress(property, owner, pointer, expression.Span, out address, out type);
    }

    private void LowerPropertyAssignment(AssignmentStatementNode a)
    {
        if (!TryLowerValue(a.Value, out var value)) return;
        TryStoreProperty(a.Target, value, _typed.TypeOf(a.Value), a.Span);
    }

    // property op= value, read through the getter and written through the setter
    private void LowerPropertyCompound(AssignmentStatementNode a, Operator plain)
    {
        if (!TryGetPropertyBinding(StripScopes(a.Target), out var property)) return;

        if (!TryLowerProperty(a.Target, out var current) || !TryLowerValue(a.Value, out var right)) return;
        if (!TryEmitBinary(plain, current, property.Type, right, _typed.TypeOf(a.Value), a.Span, out var result)) return;

        TryStoreProperty(a.Target, result, property.Type, a.Span);
    }

    public void EmitAccessor(AccessorKey key, DeclaredFunction declared)
    {
        var property = key.Property;
        var accessor = key.IsSetter ? property.Setter! : property.Getter!;

        _function = declared.Value;
        _scope = declared.Scope;
        _returnType = key.IsSetter ? AeroType.Void : property.Type;

        var offset = 0;
        if (declared.HasSelf)
        {
            _self = _function.GetParam(0);
            _self.Name = "self";
            _selfOwner = declared.Owner;
            offset = 1;
        }

        _entry = _context.AppendBasicBlock(_function, "entry");
        _builder.PositionAtEnd(_entry);

        // 'it' is the backing value, which only exists when the property needs storage
        if (_types.NeedsBacking(property))
        {
            if (!TryBackingAddress(property, _selfOwner, _self, property.Span, out _backing, out _backingType)) return;
            _backingAeroType = property.Type;
        }

        if (key.IsSetter && !BindSetterValue(accessor, property, _function.GetParam((uint)offset))) return;

        LowerStatements(accessor.Body!.Statements, true);

        if (IsTerminated()) return;
        if (_returnType == AeroType.Void) _builder.BuildRetVoid();
        else _builder.BuildUnreachable(); // the checker doesn't prove every path returns yet
    }

    // The checker declares an implicit 'value' parameter on the setter node
    private bool BindSetterValue(PropertyAccessorNode setter, PropertySymbol property, LLVMValueRef argument)
    {
        if (_typed.BindingOf(setter) is not LocalBinding { Variable: var variable })
        {
            Fail($"The setter of '{property.Name}' was not resolved", property.Span);
            return false;
        }
        if (!_types.TryLower(property.Type, _scope, property.Span, out var lowered)) return false;

        argument.Name = "value";

        var slot = EntryAlloca(lowered, "value");
        _builder.BuildStore(argument, slot);
        _locals[variable] = new LocalSlot(slot, lowered);
        return true;
    }

    // Stores the initial value of every static, const and module level field and property, in declaration order
    public void EmitStaticInitializers(LLVMValueRef function, IReadOnlyList<StaticInitializer> initializers)
    {
        _function = function;
        _returnType = AeroType.Void;

        _entry = _context.AppendBasicBlock(_function, "entry");
        _builder.PositionAtEnd(_entry);

        foreach (var initializer in initializers)
        {
            _scope = initializer.Scope;
            if (!TryLowerValue(initializer.Value, out var value)) continue;

            _builder.BuildStore(Coerce(value, _typed.TypeOf(initializer.Value), initializer.Target.AeroType, initializer.Span), initializer.Target.Address);
        }

        _builder.BuildRetVoid();
    }
}
