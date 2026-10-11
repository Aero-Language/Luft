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

        var parameters = ConstructorShapes.Of(key.Type, layout).First(s => s.Key == key).Parameters;

        if (key.Kind == ConstructorKind.Explicit)
        {
            var declarations = key.Symbol!.Declaration.Parameters;
            for (var i = 0; i < declarations.Count; i++) BindParameter(declarations[i], _function.GetParam((uint)i + 1));
        }

        // Fields that arrive as arguments are not initialized twice
        var argumentFields = key.Kind is ConstructorKind.Primary or ConstructorKind.Fields
            ? parameters.Select(p => p.Name).ToHashSet()
            : new HashSet<string>();

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

    // Field initializers run before the body, base types first. The fields in argumentFields come from the arguments instead.
    private bool InitializeFields(ConstructorKey key, TypeLayout layout, HashSet<string> argumentFields)
    {
        var chain = new List<TypeSymbol>();
        for (TypeSymbol? link = key.Type; link is not null && !chain.Contains(link); link = _types.BaseOf(link)) chain.Add(link);
        chain.Reverse();

        foreach (var type in chain)
        {
            var own = _types.LayoutOf(type, type.Span);
            if (own is null) return false;

            foreach (var field in type.Scope.Fields.Values.SelectMany(l => l).OrderBy(f => f.Span.Start))
            {
                if (field.Initializer is null || IsStaticField(field)) continue;
                if (type == key.Type && argumentFields.Contains(field.Name)) continue;
                if (own.Find(field.Name) is not { } slot) continue; // a base layout is a prefix, so its slot index fits the derived struct too

                if (!TryLowerValue(field.Initializer, out var value)) return false;

                var address = _builder.BuildStructGEP2(layout.StructType, _self, (uint)slot.Index, field.Name);
                _builder.BuildStore(Coerce(value, _typed.TypeOf(field.Initializer), slot.Type, field.Span), address);
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

    private bool TryLowerConstructorCall(CallExpressionNode call, ConstructorBinding binding, out LLVMValueRef value)
    {
        value = default;

        var type = binding.Type;
        var key = ConstructorShapes.Select(type, binding.Constructor, call.Arguments.Count);
        if (!_constructors.TryGetValue(key, out var declared))
        {
            Fail($"'{type.Name}' cannot be constructed yet, only non generic classes, structs and records can", call.Span);
            return false;
        }

        var layout = _types.LayoutOf(type, call.Span);
        if (layout is null) return false;

        var parameters = ConstructorShapes.Of(type, layout).First(s => s.Key == key).Parameters;

        LLVMValueRef pointer;
        if (type is ClassSymbol) pointer = AllocateObject(layout);
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

    // Zeroed memory with the reference count set to 1, the vtable slot follows with inheritance in sub-task 6e
    private LLVMValueRef AllocateObject(TypeLayout layout)
    {
        var pointer = CallRuntime("aero_alloc", _types.Ptr, layout.StructType.SizeOf);

        var count = _builder.BuildStructGEP2(layout.StructType, pointer, (uint)TypeLowering.RefCountSlot, "refs");
        _builder.BuildStore(LLVMValueRef.CreateConstInt(_context.Int32Type, 1), count);
        return pointer;
    }
}
