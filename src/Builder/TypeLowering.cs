using LLVMSharp.Interop;
using Luft.Ast;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

public sealed record LayoutSlot(string Name, AeroType Type, int Index);

public sealed class TypeLayout(LLVMTypeRef structType, IReadOnlyList<LLVMTypeRef> elements, IReadOnlyList<LayoutSlot> slots)
{
    public LLVMTypeRef StructType { get; } = structType;
    public IReadOnlyList<LLVMTypeRef> Elements { get; } = elements;
    public IReadOnlyList<LayoutSlot> Slots { get; } = slots;

    // The last match wins, so a derived field hides an inherited one
    public LayoutSlot? Find(string name) => Slots.LastOrDefault(s => s.Name == name);
}

// Maps Aero types to LLVM types. Nothing here is generic aware: generic types fail until monomorphization exists.
public sealed class TypeLowering : AeroThrower
{
    public const int VTableSlot = 0;
    public const int RefCountSlot = 1;

    protected override CompilerStage Stage => CompilerStage.CodeGen;

    private readonly LLVMContextRef _context;
    private readonly TypeTable _table;
    private readonly Dictionary<string, LLVMTypeRef> _primitives = new();
    private readonly Dictionary<TypeSymbol, TypeLayout> _layouts = new();
    private readonly HashSet<TypeSymbol> _building = [];

    public bool HasErrors { get; private set; }

    public LLVMTypeRef Ptr { get; }
    public LLVMTypeRef StringStruct { get; }
    public LLVMTypeRef RangeStruct { get; }
    public LLVMTypeRef ArrayStruct { get; }
    public LLVMTypeRef LambdaStruct { get; }
    public LLVMTypeRef TraitStruct { get; }

    public TypeLowering(LLVMContextRef context, TypeTable table)
    {
        _context = context;
        _table = table;

        var i32 = context.Int32Type;
        Ptr = LLVMTypeRef.CreatePointer(context.Int8Type, 0);

        StringStruct = Named("String", Ptr, i32); // UTF-16 data, length in UTF-16 units
        RangeStruct = Named("Range", i32, i32);   // start, end
        ArrayStruct = Named("Array", Ptr, i32);   // data, length
        LambdaStruct = Named("Lambda", Ptr, Ptr); // function, environment
        TraitStruct = Named("TraitRef", Ptr, Ptr); // data, vtable

        _primitives[AeroType.Int.Name] = i32;
        _primitives[AeroType.Float.Name] = context.FloatType;
        _primitives[AeroType.Bool.Name] = context.Int8Type; // i8 so it stays addressable
        _primitives[AeroType.Byte.Name] = context.Int8Type;
        _primitives[AeroType.Char.Name] = context.Int16Type; // UTF-16 code unit
        _primitives[AeroType.Void.Name] = context.VoidType;
        _primitives[AeroType.String.Name] = StringStruct;
        _primitives[AeroType.Range.Name] = RangeStruct;
    }

    private LLVMTypeRef Named(string name, params LLVMTypeRef[] elements)
    {
        var type = _context.CreateNamedStruct(name);
        type.StructSetBody(elements, false);
        return type;
    }

    private void Fail(string message, SourceSpan span)
    {
        HasErrors = true;
        Error(message, span);
    }

    private static bool IsVoid(LLVMTypeRef type) => type.Kind == LLVMTypeKind.LLVMVoidTypeKind;

    // Reports and returns false when the type can't be lowered
    public bool TryLower(AeroType type, TypeScope scope, SourceSpan span, out LLVMTypeRef result)
    {
        result = Ptr;
        if (!Core(type, scope, span, out var core, out var isClass)) return false;

        if (type.IsRef)
        {
            result = Ptr; // ref T is an alias to the caller's value
            return true;
        }

        // Nullable classes use a null pointer, everything else carries a flag
        result = type.IsNullable && !isClass && !IsVoid(core)
            ? _context.GetStructType([_context.Int8Type, core], false)
            : core;
        return true;
    }

    public LLVMTypeRef? LowerFunction(AeroType returnType, IEnumerable<AeroType> parameters, TypeScope scope, SourceSpan span)
    {
        var ok = TryLower(returnType, scope, span, out var ret);
        var args = new List<LLVMTypeRef>();

        foreach (var p in parameters)
        {
            if (!TryLower(p, scope, span, out var lowered))
            {
                ok = false;
                continue;
            }
            if (IsVoid(lowered))
            {
                Fail("A parameter cannot be 'Void'", span);
                ok = false;
                continue;
            }
            args.Add(lowered);
        }

        return ok ? LLVMTypeRef.CreateFunction(ret, args.ToArray(), false) : null;
    }

    private bool Core(AeroType type, TypeScope scope, SourceSpan span, out LLVMTypeRef result, out bool isClass)
    {
        result = Ptr;
        isClass = false;

        switch (type)
        {
            case ScalarType s:
                return Scalar(s, scope, span, out result, out isClass);
            case ArrayType a:
                if (!TryLower(a.ElementType, scope, span, out _)) return false;
                result = ArrayStruct;
                return true;
            case LambdaType l:
                var ok = TryLower(l.ReturnType, scope, span, out _);
                foreach (var p in l.Parameters) ok &= TryLower(p.Type, scope, span, out _);
                result = LambdaStruct;
                return ok;
            case GenericType g:
                Fail($"Generic type '{g}' cannot be lowered yet, generics need monomorphization", span);
                return false;
            case GenericParameterType gp:
                Fail($"Generic parameter '{gp.Name}' needs a concrete type before it can be lowered", span);
                return false;
            case SpecialType when type == AeroType.Null:
                return true;
            case SpecialType:
                Fail($"The type '{type}' could not be inferred", span);
                return false;
            default:
                Fail($"The type '{type}' cannot be lowered", span);
                return false;
        }
    }

    private bool Scalar(ScalarType s, TypeScope scope, SourceSpan span, out LLVMTypeRef result, out bool isClass)
    {
        result = Ptr;
        isClass = false;

        if (_primitives.TryGetValue(s.Name, out var primitive))
        {
            result = primitive;
            return true;
        }

        if (scope.AllGenerics().Contains(s.Name))
        {
            Fail($"Generic parameter '{s.Name}' needs a concrete type before it can be lowered", span);
            return false;
        }

        var symbol = Find(s.Name, scope, 0);
        if (symbol is null)
        {
            Fail($"Type '{s.Name}' could not be lowered, it is unknown or an unresolved generic parameter", span);
            return false;
        }

        switch (symbol)
        {
            case ClassSymbol:
                isClass = true;
                return true;
            case EnumSymbol:
                result = _context.Int32Type; // the ordinal, member values are looked up separately
                return true;
            case TraitSymbol:
                result = TraitStruct;
                return true;
            case StructSymbol or RecordSymbol:
                var layout = LayoutOf(symbol, span);
                if (layout is null) return false;
                result = layout.StructType;
                return true;
            default:
                Fail($"'{symbol.Name}' cannot be used as a type of a value", span);
                return false;
        }
    }

    public TypeLayout? LayoutOf(TypeSymbol symbol, SourceSpan span)
    {
        if (_layouts.TryGetValue(symbol, out var cached)) return cached;

        if (symbol is not (ClassSymbol or StructSymbol or RecordSymbol))
        {
            Fail($"'{symbol.Name}' has no memory layout", span);
            return null;
        }
        if (symbol.GenericParameters.Count > 0)
        {
            Fail($"'{symbol.Name}' is generic and cannot be laid out yet, generics need monomorphization", span);
            return null;
        }
        if (!_building.Add(symbol))
        {
            Fail($"'{symbol.Name}' contains itself by value", span);
            return null;
        }

        try
        {
            return BuildLayout(symbol, span);
        }
        finally
        {
            _building.Remove(symbol);
        }
    }

    private TypeLayout? BuildLayout(TypeSymbol symbol, SourceSpan span)
    {
        var elements = new List<LLVMTypeRef>();
        var slots = new List<LayoutSlot>();

        if (!TryBase(symbol, span, out var baseSymbol)) return null;

        if (baseSymbol is not null)
        {
            // The base layout is a prefix, so a base pointer is valid for derived types
            var baseLayout = LayoutOf(baseSymbol, span);
            if (baseLayout is null) return null;
            elements.AddRange(baseLayout.Elements);
            slots.AddRange(baseLayout.Slots);
        }
        else if (symbol is ClassSymbol)
        {
            elements.Add(Ptr);                 // vtable, holds the destructor
            elements.Add(_context.Int32Type);  // reference count
        }
        // TODO: structs get a vtable pointer once virtual or trait dispatch on structs is lowered

        var ok = true;
        foreach (var (name, type, at) in MembersOf(symbol))
        {
            if (!TryLower(type, symbol.Scope, at, out var lowered))
            {
                ok = false;
                continue;
            }
            if (IsVoid(lowered))
            {
                Fail($"'{name}' cannot have the type 'Void'", at);
                ok = false;
                continue;
            }

            slots.Add(new LayoutSlot(name, type, elements.Count));
            elements.Add(lowered);
        }
        if (!ok) return null;

        var structName = symbol.Module.ModulePath.Length == 0 ? symbol.Name : $"{symbol.Module.ModulePath}.{symbol.Name}";
        var layout = new TypeLayout(Named(structName, elements.ToArray()), elements, slots);
        _layouts[symbol] = layout;
        return layout;
    }

    // Instance fields and properties that need storage, in declaration order
    private IEnumerable<(string Name, AeroType Type, SourceSpan Span)> MembersOf(TypeSymbol symbol)
    {
        var members = new List<(string Name, AeroType Type, SourceSpan Span)>();

        foreach (var f in symbol.Scope.Fields.Values.SelectMany(l => l))
        {
            if (f.Declaration.MemberMods.HasFlag(MemberMod.Static) || f.VarKind == VariableKind.Const) continue;

            var type = f.Type;
            if (type.IsAuto && f.Initializer is not null && _table.Typed.TypeOf(f.Initializer) is { } inferred) type = inferred;
            members.Add((f.Name, type, f.Span));
        }

        foreach (var p in symbol.Scope.Properties.Values.SelectMany(l => l))
        {
            if (p.Declaration.MemberMods.HasFlag(MemberMod.Static) || !NeedsBacking(p)) continue;
            members.Add(($"<backing>{p.Name}", p.Type, p.Span));
        }

        return members.OrderBy(m => m.Span.Start);
    }

    // TODO: accessors that use 'it' need storage too, that needs a scan of the accessor bodies
    private static bool NeedsBacking(PropertySymbol p)
        => p.Initializer is not null || p.Getter is { Body: null } || p.Setter is { Body: null };

    // Only the first listed type can be a base, the rest are traits
    private bool TryBase(TypeSymbol symbol, SourceSpan span, out TypeSymbol? baseSymbol)
    {
        baseSymbol = null;

        IEnumerable<AeroType> bases = symbol switch
        {
            ClassSymbol c => c.Node.Implements,
            StructSymbol s => s.Node.Implements,
            RecordSymbol r => r.Node.Implements,
            _ => []
        };

        var first = bases.FirstOrDefault();
        var name = first switch
        {
            ScalarType s => s.Name,
            GenericType g => g.Definition.Name,
            _ => null
        };
        if (name is null) return true;

        var arity = first is GenericType generic ? generic.TypeArguments.Count : 0;
        var found = Find(name, symbol.Scope, arity);
        if (found is null) return true; // the resolver reports unknown types

        var kindMatches = symbol is ClassSymbol ? found is ClassSymbol : found is StructSymbol or RecordSymbol;
        if (!kindMatches) return true;

        if (arity > 0)
        {
            Fail($"The generic base type '{first}' cannot be lowered yet, generics need monomorphization", span);
            return false;
        }

        baseSymbol = found;
        return true;
    }

    private TypeSymbol? Find(string name, TypeScope scope, int arity)
    {
        name = name[(name.LastIndexOf('.') + 1)..];

        for (var s = scope; s is not null; s = s.ContainingScope)
            if (Match(s, name, arity) is { } hit) return hit;

        // TODO: respect imports instead of searching every module
        foreach (var module in _table.Modules.Values)
            if (Match(module.Scope, name, arity) is { } found) return found;

        return null;
    }

    private static TypeSymbol? Match(TypeScope scope, string name, int arity)
        => scope.Types.TryGetValue(name, out var list) ? list.FirstOrDefault(t => t.GenericParameters.Count == arity) : null;
}