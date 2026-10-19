using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

// The functions a library emits for its consumers: allocating `_New` wrappers, default argument entry points and field accessors
public sealed partial class FunctionEmitter
{
    // Forwards the first `Arity` arguments and fills in the defaults of the others; a constructor entry allocates the object, runs the init function on it and returns it
    public void EmitEntry(DefaultEntry entry)
        => WithInstance(entry.Owner, null, null, () => EmitEntryCore(entry));

    private void EmitEntryCore(DefaultEntry entry)
    {
        _function = entry.Wrapper.Value;
        _scope = entry.Wrapper.Scope;
        _returnType = AeroType.Void;

        _entry = _context.AppendBasicBlock(_function, "entry");
        _builder.PositionAtEnd(_entry);

        var arguments = new List<LLVMValueRef>();
        var offset = 0;
        LLVMValueRef pointer = default;

        if (entry.Constructor is not null)
        {
            var layout = _types.LayoutOf(entry.Owner!, entry.Owner!.Span);
            if (layout is null) return;

            pointer = AllocateObject(entry.Owner, layout);
            _self = pointer;
            _selfOwner = entry.Owner;
            arguments.Add(pointer);
        }
        else if (entry.Wrapper.HasSelf)
        {
            _self = _function.GetParam(0);
            _self.Name = "self";
            _selfOwner = entry.Owner;
            arguments.Add(_self);
            offset = 1;
        }

        var span = entry.Owner?.Span ?? SourceSpan.Unknown;
        for (var i = 0; i < entry.Parameters.Count; i++)
        {
            if (i < entry.Arity)
            {
                arguments.Add(_function.GetParam((uint)(i + offset)));
                continue;
            }

            var parameter = entry.Parameters[i];
            if (!TryLowerArgument(parameter.Initializer, parameter.Type, parameter.Name, parameter.Initializer?.Span ?? span, out var value)) return;
            arguments.Add(value);
        }

        var isVoid = entry.Target.Type.ReturnType.Kind == LLVMTypeKind.LLVMVoidTypeKind;
        var call = _builder.BuildCall2(entry.Target.Type, entry.Target.Value, arguments.ToArray(), isVoid || entry.Constructor is not null ? "" : "call");

        if (entry.Constructor is not null) _builder.BuildRet(pointer);
        else if (isVoid) _builder.BuildRetVoid();
        else _builder.BuildRet(call);
    }

    // get_Name and set_Name read and write the field's slot, the consumer does not know the layout
    public void EmitFieldAccessor(FieldSymbol field, bool isSetter, DeclaredFunction declared)
        => WithInstance(declared.Owner, null, null, () => EmitFieldAccessorCore(field, isSetter, declared));

    private void EmitFieldAccessorCore(FieldSymbol field, bool isSetter, DeclaredFunction declared)
    {
        _function = declared.Value;
        _scope = declared.Scope;

        _entry = _context.AppendBasicBlock(_function, "entry");
        _builder.PositionAtEnd(_entry);

        var owner = declared.Owner!;
        var layout = _types.LayoutOf(owner, field.Span);
        if (layout is null) return;

        if (layout.Find(field.Name) is not { } slot)
        {
            Fail($"The field '{field.Name}' has no slot to read", field.Span);
            return;
        }

        var address = _builder.BuildStructGEP2(layout.StructType, _function.GetParam(0), (uint)slot.Index, field.Name);
        if (isSetter)
        {
            _builder.BuildStore(_function.GetParam(1), address);
            _builder.BuildRetVoid();
        }
        else _builder.BuildRet(_builder.BuildLoad2(layout.Elements[slot.Index], address, field.Name));
    }
}
