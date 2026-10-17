using LLVMSharp.Interop;
using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

// A constructor is an init function that fills in an already allocated object; the call site allocates (heap for classes, stack slot for structs)
public sealed partial class FunctionEmitter
{
    public void EmitConstructor(ConstructorKey key, DeclaredFunction declared)
        => WithInstance(key.Type, null, null, () => EmitConstructorCore(key, declared));

    private void EmitConstructorCore(ConstructorKey key, DeclaredFunction declared)
    {
        _function = declared.Value;
        _scope = declared.Scope;
        _returnType = AeroType.Void;
        _self = _function.GetParam(0);
        _self.Name = "self";
        _selfOwner = key.Type;

        _entry = _context.AppendBasicBlock(_function, "entry");
        _builder.PositionAtEnd(_entry);

        var layout = _types.LayoutOf(key.Type, key.Type.Span);
        if (layout is null) return;

        var parameters = ConstructorShapes.Of(key.Type, layout, _types.InfoOf(key.Type)?.Arguments).First(s => s.Key == key).Parameters;

        if (key.Kind == ConstructorKind.Explicit)
        {
            var declarations = key.Symbol!.Declaration.Parameters;
            for (var i = 0; i < declarations.Count; i++) BindParameter(declarations[i], _function.GetParam((uint)i + 1));
        }
        else if (key.Kind == ConstructorKind.Primary && BaseDeclarationOf(key.Type).Arguments is not null)
            BindPrimaryArguments(key.Type, parameters);

        // Fields that arrive as arguments are not initialized twice
        var argumentFields = key.Kind is ConstructorKind.Primary or ConstructorKind.Fields
            ? parameters.Select(p => p.Name).ToHashSet()
            : new HashSet<string>();

        // The base constructor fills in the inherited part first, then this type's own initializers run
        if (!TryCallBaseConstructor(key)) return;
        if (!InitializeFields(key, layout, argumentFields)) return;

        switch (key.Kind)
        {
            case ConstructorKind.Primary or ConstructorKind.Fields:
                StoreParameters(parameters, layout);
                break;
            case ConstructorKind.Explicit when key.Symbol!.Body is { } body:
                LowerStatements(body.Statements, true);
                break;
        }

        if (!IsTerminated()) _builder.BuildRetVoid();
    }

    // The `: Base(args)` of a type's declaration with the node the checker bound it on
    private static (AstNode? Node, ValueList<ExpressionNode>? Arguments) BaseDeclarationOf(TypeSymbol type) => type switch
    {
        ClassSymbol c => (c.Node, c.Node.BaseArguments),
        StructSymbol s => (s.Node, s.Node.BaseArguments),
        RecordSymbol r => (r.Node, r.Node.BaseArguments),
        _ => (null, null)
    };

    // The arguments of a primary constructor are plain locals while the base constructor call is lowered
    private void BindPrimaryArguments(TypeSymbol type, ValueList<ParamSymbol> parameters)
    {
        var variables = type.Scope.PrimaryConstructor!.Variables;
        for (var i = 0; i < variables.Count && i < parameters.Count; i++)
        {
            if (_typed.BindingOf(variables[i]) is not LocalBinding { Variable: var variable }) continue;
            if (!_types.TryLower(parameters[i].Type, _scope, variables[i].Span, out var valueType)) return;

            var argument = _function.GetParam((uint)i + 1);
            argument.Name = parameters[i].Name;

            var slot = EntryAlloca(valueType, parameters[i].Name);
            _builder.BuildStore(argument, slot);
            _locals[variable] = new LocalSlot(slot, valueType);
        }
    }

    // Runs the base type's constructor on the same object: the one named by `: base(...)`, otherwise one that needs no arguments
    private bool TryCallBaseConstructor(ConstructorKey key)
    {
        var baseType = _types.BaseOf(key.Type);
        if (baseType is null) return true;

        var span = key.Symbol?.Span ?? key.Type.Span;
        var baseLayout = _types.LayoutOf(baseType, span);
        if (baseLayout is null) return false;

        ValueList<ExpressionNode>? arguments = null;
        ConstructorSymbol? named = null;
        var resolved = false;

        if (key.Kind == ConstructorKind.Explicit)
        {
            arguments = key.Symbol!.Declaration.BaseArguments;
            if (_typed.BindingOf(key.Symbol.Declaration) is ConstructorBinding explicitBinding) (named, resolved) = (explicitBinding.Constructor, true);
        }
        else if (key.Kind == ConstructorKind.Primary)
        {
            var (node, primaryArguments) = BaseDeclarationOf(key.Type);
            arguments = primaryArguments;
            if (node is not null && _typed.BindingOf(node) is ConstructorBinding primaryBinding) (named, resolved) = (primaryBinding.Constructor, true);
        }

        if (named is not null) named = _types.MapMember(baseType, named); // the checker bound the constructor of the generic definition
        var shapes = ConstructorShapes.Of(baseType, baseLayout, _types.InfoOf(baseType)?.Arguments);
        ConstructorKey baseKey;
        if (arguments is not null)
        {
            if (!resolved)
            {
                Fail($"The base constructor call of '{key.Type.Name}' was not resolved", span);
                return false;
            }
            baseKey = ConstructorShapes.Select(baseType, named, arguments.Count);
        }
        else
        {
            var implicitShape = shapes.FirstOrDefault(s => s.Parameters.All(p => p.Initializer is not null));
            if (implicitShape.Parameters is null)
            {
                Fail($"'{baseType.Name}' has no constructor without parameters to run before '{key.Type.Name}'", span);
                return false;
            }
            baseKey = implicitShape.Key;
        }

        if (!_constructors.TryGetValue(baseKey, out var declared))
        {
            Fail($"The constructor of '{baseType.Name}' could not be found", span);
            return false;
        }

        var parameters = shapes.First(s => s.Key == baseKey).Parameters;
        var values = new List<LLVMValueRef> { _self };
        for (var i = 0; i < parameters.Count; i++)
        {
            var source = arguments is not null && i < arguments.Count ? arguments[i] : parameters[i].Initializer;
            if (!TryLowerArgument(source, parameters[i].Type, parameters[i].Name, span, out var value)) return false;
            values.Add(value);
        }

        _builder.BuildCall2(declared.Type, declared.Value, values.ToArray(), "");
        return true;
    }

    // Field initializers run before the body, base types are already done by their own constructor.
    // The fields in argumentFields come from the arguments instead.
    private bool InitializeFields(ConstructorKey key, TypeLayout layout, HashSet<string> argumentFields)
    {
        var chain = new List<TypeSymbol> { key.Type };

        foreach (var type in chain)
        {
            var own = _types.LayoutOf(type, type.Span);
            if (own is null) return false;

            // Fields and properties with an initializer, in declaration order; a property stores straight into its backing value
            var members = type.Scope.Fields.Values.SelectMany(l => l)
                .Where(f => f.Initializer is not null && !IsStaticField(f) && !(type == key.Type && argumentFields.Contains(f.Name)))
                .Select(f => (Start: f.Span.Start, Name: f.Name, Initializer: f.Initializer!, Span: f.Span))
                .Concat(type.Scope.Properties.Values.SelectMany(l => l)
                    .Where(p => p.Initializer is not null && !p.Declaration.MemberMods.HasFlag(MemberMod.Static))
                    .Select(p => (Start: p.Span.Start, Name: $"<backing>{p.Name}", Initializer: p.Initializer!, Span: p.Span)))
                .OrderBy(m => m.Start);

            foreach (var member in members)
            {
                if (own.Find(member.Name) is not { } slot) continue; // a base layout is a prefix, so its slot index fits the derived struct too

                if (!TryLowerValue(member.Initializer, out var value)) return false;

                var address = _builder.BuildStructGEP2(layout.StructType, _self, (uint)slot.Index, member.Name);
                _builder.BuildStore(Coerce(value, TypeOf(member.Initializer), slot.Type, member.Span), address);
            }
        }
        return true;
    }

    // The parameters of a primary or record constructor are its fields
    private void StoreParameters(ValueList<ParamSymbol> parameters, TypeLayout layout)
    {
        for (var i = 0; i < parameters.Count; i++)
        {
            if (layout.Find(parameters[i].Name) is not { } slot) continue;

            var argument = _function.GetParam((uint)i + 1);
            argument.Name = parameters[i].Name;

            var address = _builder.BuildStructGEP2(layout.StructType, _self, (uint)slot.Index, parameters[i].Name);
            _builder.BuildStore(argument, address);
        }
    }

    // Runs the destructor body, then the destroy function of the base class. Releasing the fields follows with the runtime work.
    public void EmitDestroyer(ClassSymbol type, DeclaredFunction declared)
        => WithInstance(type, null, null, () => EmitDestroyerCore(type, declared));

    private void EmitDestroyerCore(ClassSymbol type, DeclaredFunction declared)
    {
        _function = declared.Value;
        _scope = declared.Scope;
        _returnType = AeroType.Void;
        _self = _function.GetParam(0);
        _self.Name = "self";
        _selfOwner = type;

        _entry = _context.AppendBasicBlock(_function, "entry");
        _builder.PositionAtEnd(_entry);

        if (type.Scope.Destructor is { Body: { } body }) LowerStatements(body.Statements);

        if (!IsTerminated() && _types.BaseOf(type) is { } baseType && _members.Destroyers.TryGetValue(baseType, out var baseDestroy))
            _builder.BuildCall2(baseDestroy.Type, baseDestroy.Value, [_self]);

        if (!IsTerminated()) _builder.BuildRetVoid();
    }

    private bool TryLowerConstructorCall(CallExpressionNode call, ConstructorBinding binding, out LLVMValueRef value)
    {
        value = default;

        // A generic type is constructed as the instance its type arguments name
        var type = binding.Type;
        var constructor = binding.Constructor;
        if (type.GenericParameters.Count > 0)
        {
            var created = TypeOf(call) is { } constructed ? _types.SymbolOf(constructed, _scope) : null;
            if (created is null || _types.InfoOf(created)?.Definition != type)
            {
                Fail($"The type arguments of '{type.Name}' could not be worked out here", call.Span);
                return false;
            }

            type = created;
            constructor = _types.MapMember(created, constructor);
        }

        var key = ConstructorShapes.Select(type, constructor, call.Arguments.Count);
        if (!_constructors.TryGetValue(key, out var declared))
        {
            Fail($"'{type.Name}' cannot be constructed, no matching constructor was declared", call.Span);
            return false;
        }

        var layout = _types.LayoutOf(type, call.Span);
        if (layout is null) return false;

        var parameters = ConstructorShapes.Of(type, layout, _types.InfoOf(type)?.Arguments).First(s => s.Key == key).Parameters;

        LLVMValueRef pointer;
        if (type is ClassSymbol) pointer = AllocateObject(type, layout);
        else
        {
            pointer = EntryAlloca(layout.StructType, "obj");
            _builder.BuildStore(LLVMValueRef.CreateConstNull(layout.StructType), pointer); // fields without an initializer start as zero
        }

        var arguments = new List<LLVMValueRef> { pointer };
        for (var i = 0; i < parameters.Count; i++)
        {
            var source = i < call.Arguments.Count ? call.Arguments[i] : parameters[i].Initializer;
            if (!TryLowerArgument(source, parameters[i].Type, parameters[i].Name, call.Span, out var argument)) return false;
            arguments.Add(argument);
        }

        _builder.BuildCall2(declared.Type, declared.Value, arguments.ToArray(), "");

        value = type is ClassSymbol ? pointer : _builder.BuildLoad2(layout.StructType, pointer, "new");
        return true;
    }

    // Zeroed memory with the vtable of the class and the reference count set to 1
    private LLVMValueRef AllocateObject(TypeSymbol type, TypeLayout layout)
    {
        var pointer = CallRuntime("aero_alloc", _types.Ptr, layout.StructType.SizeOf);

        if (_members.VTables.TryGetValue(type, out var vtable))
        {
            var slot = _builder.BuildStructGEP2(layout.StructType, pointer, (uint)TypeLowering.VTableSlot, "vtable");
            _builder.BuildStore(vtable, slot);
        }

        var count = _builder.BuildStructGEP2(layout.StructType, pointer, (uint)TypeLowering.RefCountSlot, "refs");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 1), count);
        return pointer;
    }
}
