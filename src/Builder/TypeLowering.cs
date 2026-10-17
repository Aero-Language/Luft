using LLVMSharp.Interop;
using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

public sealed record LayoutSlot(string Name, AeroType Type, int Index);

// The class, struct or record made for one set of type arguments of a generic type. It shares the declaration nodes of its definition,
// `Members` maps every member symbol of the definition to the copy living in the instance's scope.
public sealed class InstanceInfo(TypeSymbol definition, Dictionary<string, AeroType> arguments, Dictionary<string, TypeSymbol> pins, string key)
{
    public TypeSymbol Definition { get; } = definition;
    public Dictionary<string, AeroType> Arguments { get; } = arguments;
    public Dictionary<string, TypeSymbol> Pins { get; } = pins;
    public string Key { get; } = key;
    public Dictionary<object, object> Members { get; } = new(ReferenceEqualityComparer.Instance);

    public string DisplayName => $"{Definition.Name}<{Key}>";
}

public sealed class TypeLayout(LLVMTypeRef structType, IReadOnlyList<LLVMTypeRef> elements, IReadOnlyList<LayoutSlot> slots)
{
    public LLVMTypeRef StructType { get; } = structType;
    public IReadOnlyList<LLVMTypeRef> Elements { get; } = elements;
    public IReadOnlyList<LayoutSlot> Slots { get; } = slots;

    // The last match wins, so a derived field hides an inherited one
    public LayoutSlot? Find(string name) => Slots.LastOrDefault(s => s.Name == name);
}

// Maps Aero types to LLVM types. A generic class, struct or record is lowered per set of type arguments, as an instance symbol.
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
    private readonly Dictionary<(TypeSymbol Definition, string Key), TypeSymbol> _instances = new();
    private readonly Dictionary<TypeSymbol, InstanceInfo> _info = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, TypeSymbol> _memberOwners = new(ReferenceEqualityComparer.Instance);
    private readonly Queue<TypeSymbol> _created = new();
    private bool _indexed;
    private bool _flushing;
    private int _depth;

    // Called once an outermost lowering step is done, so an instance is never declared in the middle of a layout
    public Action<TypeSymbol>? OnInstance { get; set; }

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

    // The generic arguments of the function instance being emitted, applied to every type that gets lowered
    public IReadOnlyDictionary<string, AeroType>? Substitution { get; set; }

    // Types the arguments of the instance being emitted name, resolved at the call site where the function's own scope may not see them
    public IReadOnlyDictionary<string, TypeSymbol>? Pinned { get; set; }

    // Resolves every named type inside the arguments in the scope of the call, so the instance can find them again later
    public Dictionary<string, TypeSymbol> Pin(IEnumerable<AeroType> arguments, TypeScope scope)
    {
        var pins = new Dictionary<string, TypeSymbol>();
        foreach (var argument in arguments) Collect(argument, scope, pins);
        return pins;
    }

    private void Collect(AeroType type, TypeScope scope, Dictionary<string, TypeSymbol> pins)
    {
        switch (type)
        {
            case ScalarType s:
                var name = s.Name[(s.Name.LastIndexOf('.') + 1)..];
                if (!pins.ContainsKey(name) && Find(name, scope, 0) is { } symbol) pins[name] = symbol;
                break;
            case ArrayType a:
                Collect(a.ElementType, scope, pins);
                break;
            case LambdaType l:
                Collect(l.ReturnType, scope, pins);
                foreach (var p in l.Parameters) Collect(p.Type, scope, pins);
                break;
            case GenericType g:
                foreach (var a in g.TypeArguments) Collect(a, scope, pins);
                break;
        }
    }

    // Reports and returns false when the type can't be lowered
    public bool TryLower(AeroType type, TypeScope scope, SourceSpan span, out LLVMTypeRef result)
    {
        _depth++;
        try { return TryLowerCore(type, scope, span, out result); }
        finally { Leave(); }
    }

    private bool TryLowerCore(AeroType type, TypeScope scope, SourceSpan span, out LLVMTypeRef result)
    {
        result = Ptr;
        type = TypeSubstitution.Apply(type, Substitution);
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
                var instance = InstanceOf(g, scope, span, true);
                return instance is not null && LowerSymbol(instance, span, out result, out isClass);
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

        return LowerSymbol(symbol, span, out result, out isClass);
    }

    private bool LowerSymbol(TypeSymbol symbol, SourceSpan span, out LLVMTypeRef result, out bool isClass)
    {
        result = Ptr;
        isClass = false;

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

    // The declaration behind a plain named type, null for primitives, arrays, lambdas and generics
    public TypeSymbol? SymbolOf(AeroType type, TypeScope scope)
    {
        _depth++;
        try
        {
            return type switch
            {
                ScalarType s => Find(s.Name, scope, 0),
                GenericType g => InstanceOf(Normalize(g), scope, SourceSpan.Unknown, false),
                _ => null
            };
        }
        finally { Leave(); }
    }

    // The first listed type when it is a class, struct or record of the same kind
    public TypeSymbol? BaseOf(TypeSymbol symbol)
    {
        _depth++;
        try { return TryBase(symbol, symbol.Span, out var baseSymbol) ? baseSymbol : null; }
        finally { Leave(); }
    }

    public TypeLayout? LayoutOf(TypeSymbol symbol, SourceSpan span)
    {
        _depth++;
        try { return LayoutOfCore(symbol, span); }
        finally { Leave(); }
    }

    private TypeLayout? LayoutOfCore(TypeSymbol symbol, SourceSpan span)
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

        // Member types are written with the generic names of the definition, an instance substitutes its arguments
        var info = InfoOf(symbol);
        var (previousSubstitution, previousPins) = (Substitution, Pinned);
        Substitution = info?.Arguments;
        Pinned = info?.Pins;

        try
        {
            return BuildLayout(symbol, span);
        }
        finally
        {
            _building.Remove(symbol);
            Substitution = previousSubstitution;
            Pinned = previousPins;
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

            slots.Add(new LayoutSlot(name, TypeSubstitution.Apply(type, Substitution), elements.Count));
            elements.Add(lowered);
        }
        if (!ok) return null;

        var shownName = InfoOf(symbol)?.DisplayName ?? symbol.Name;
        var structName = symbol.Module.ModulePath.Length == 0 ? shownName : $"{symbol.Module.ModulePath}.{shownName}";
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

    // A property needs storage when it is automatic, has an initializer or its accessors use 'it'
    public bool NeedsBacking(PropertySymbol p)
        => p.Initializer is not null || p.Getter is { Body: null } || p.Setter is { Body: null } || UsesIt(p);

    // 'it' inside an accessor body is the backing value, found through the typed 'it' literals that lie inside the body
    private bool UsesIt(PropertySymbol p)
    {
        foreach (var body in new[] { p.Getter?.Body, p.Setter?.Body })
        {
            if (body is null) continue;

            foreach (var node in _table.Typed.Types.Keys)
                if (node is LiteralExpressionNode { LiteralType: TokenType.ItLiteral } literal
                    && literal.Span.FilePath == body.Span.FilePath
                    && body.Span.Contains(literal.Span.Start))
                    return true;
        }
        return false;
    }

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
            // The base is written with the generic names of the deriving type, an instance of it substitutes its arguments first
            var applied = TypeSubstitution.Apply(first!, InfoOf(symbol)?.Arguments);
            if (applied is not GenericType appliedGeneric || InstanceOf(appliedGeneric, symbol.Scope, span, true) is not { } instance) return false;

            baseSymbol = instance;
            return true;
        }

        baseSymbol = found;
        return true;
    }

    public InstanceInfo? InfoOf(TypeSymbol? symbol) => symbol is not null && _info.TryGetValue(symbol, out var info) ? info : null;

    // The generic type a member symbol was declared in, null for members of anything else
    public TypeSymbol? DefinitionOfMember(object member)
    {
        if (!_indexed)
        {
            _indexed = true;
            foreach (var module in _table.Modules.Values) IndexMembers(module.Scope);
        }
        return _memberOwners.GetValueOrDefault(member);
    }

    // The copy of a definition's member that lives in an instance, the member itself when there is none
    public T MapMember<T>(TypeSymbol? owner, T member) where T : class
        => member is not null && InfoOf(owner) is { } info && info.Members.TryGetValue(member, out var copy) ? (T)copy : member;

    private void IndexMembers(TypeScope scope)
    {
        foreach (var symbol in scope.Types.Values.SelectMany(l => l))
        {
            if (symbol.GenericParameters.Count > 0 && symbol is ClassSymbol or StructSymbol or RecordSymbol)
            {
                foreach (var m in symbol.Scope.Functions.Values.SelectMany(l => l)) _memberOwners[m] = symbol;
                foreach (var m in symbol.Scope.Properties.Values.SelectMany(l => l)) _memberOwners[m] = symbol;
                foreach (var m in symbol.Scope.Fields.Values.SelectMany(l => l)) _memberOwners[m] = symbol;
                foreach (var m in symbol.Scope.Operators.Values.SelectMany(l => l)) _memberOwners[m] = symbol;
                foreach (var m in symbol.Scope.Constructors) _memberOwners[m] = symbol;
            }
            IndexMembers(symbol.Scope);
        }
    }

    private GenericType Normalize(GenericType type) => TypeSubstitution.Apply(type, Substitution) as GenericType ?? type;

    private void Leave()
    {
        if (--_depth == 0) Flush();
    }

    // Declares the instances created so far, which can create more
    private void Flush()
    {
        if (_flushing) return;

        _flushing = true;
        try
        {
            while (_created.Count > 0) OnInstance?.Invoke(_created.Dequeue());
        }
        finally { _flushing = false; }
    }

    // The instance a generic class, struct or record type stands for; `report` says whether a type that is no such thing is an error
    private TypeSymbol? InstanceOf(GenericType type, TypeScope scope, SourceSpan span, bool report)
    {
        var definition = Find(type.Definition.Name, scope, type.TypeArguments.Count);
        if (definition is not (ClassSymbol or StructSymbol or RecordSymbol) || definition.GenericParameters.Count == 0)
        {
            if (report) Fail($"'{type}' is not a generic class, struct or record, other generic types cannot be lowered yet", span);
            return null;
        }

        var arguments = new List<AeroType>();
        foreach (var argument in type.TypeArguments)
        {
            if (TypeText.Parse(argument) is not { } parsed)
            {
                Fail($"The type argument '{argument.Name}' of '{type}' cannot be lowered yet", span);
                return null;
            }
            arguments.Add(parsed);
        }

        return Instantiate(definition, arguments, scope, span);
    }

    // One instance per distinct set of arguments, created on first use
    public TypeSymbol? Instantiate(TypeSymbol definition, IReadOnlyList<AeroType> arguments, TypeScope scope, SourceSpan span)
    {
        _depth++;
        try { return InstantiateCore(definition, arguments, scope, span); }
        finally { Leave(); }
    }

    private TypeSymbol? InstantiateCore(TypeSymbol definition, IReadOnlyList<AeroType> arguments, TypeScope scope, SourceSpan span)
    {
        if (arguments.Count != definition.GenericParameters.Count)
        {
            Fail($"'{definition.Name}' takes {definition.GenericParameters.Count} type arguments but got {arguments.Count}", span);
            return null;
        }

        var map = new Dictionary<string, AeroType>();
        for (var i = 0; i < arguments.Count; i++)
        {
            if (arguments[i].IsAuto || arguments[i].IsError || arguments[i] is GenericParameterType)
            {
                Fail($"The type argument '{definition.GenericParameters[i].Name}' of '{definition.Name}' could not be inferred", span);
                return null;
            }
            map[definition.GenericParameters[i].Name] = arguments[i];
        }

        var key = TypeSubstitution.Key(definition.GenericParameters, map);
        if (_instances.TryGetValue((definition, key), out var known)) return known;

        var info = new InstanceInfo(definition, map, Pin(map.Values, scope), key);
        var instance = CloneType(definition, info);

        _instances[(definition, key)] = instance;
        _info[instance] = info;
        _created.Enqueue(instance);
        return instance;
    }

    // A non generic copy of the definition: same declaration nodes, its own scope of member symbols
    private static TypeSymbol CloneType(TypeSymbol definition, InstanceInfo info)
    {
        var scope = new TypeScope(definition.Scope.ContainingScope)
        {
            PrimaryConstructor = definition.Scope.PrimaryConstructor,
            Destructor = definition.Scope.Destructor
        };

        foreach (var (name, list) in definition.Scope.Functions)
            scope.Functions[name] = list.Select(f => Copy(info, f, new FunctionSymbol(f.Name, f.Access, f.Module, f.Declaration) { ExtensionTarget = f.ExtensionTarget })).ToList();
        foreach (var (name, list) in definition.Scope.Properties)
            scope.Properties[name] = list.Select(p => Copy(info, p, new PropertySymbol(p.Name, p.Access, p.Declaration) { ExtensionTarget = p.ExtensionTarget })).ToList();
        foreach (var (name, list) in definition.Scope.Fields)
            scope.Fields[name] = list.Select(f => Copy(info, f, new FieldSymbol(f.Name, f.Access, f.Declaration))).ToList();
        foreach (var (op, list) in definition.Scope.Operators)
            scope.Operators[op] = list.Select(o => Copy(info, o, new OperatorSymbol(o.Op, o.Access, o.Module, o.Declaration))).ToList();
        foreach (var c in definition.Scope.Constructors)
            scope.Constructors.Add(Copy(info, c, new ConstructorSymbol(c.Access, c.Declaration)));

        var none = ValueList<GenericParameterType>.Empty;
        return definition switch
        {
            ClassSymbol c => new ClassSymbol(c.Node, c.Module, definition.Scope.ContainingScope) { GenericParameters = none, Scope = scope },
            StructSymbol s => new StructSymbol(s.Node, s.Module, definition.Scope.ContainingScope) { GenericParameters = none, Scope = scope },
            RecordSymbol r => new RecordSymbol(r.Node, r.Module, definition.Scope.ContainingScope) { GenericParameters = none, Scope = scope },
            _ => throw new InvalidOperationException($"'{definition.Name}' cannot be instantiated")
        };
    }

    private static T Copy<T>(InstanceInfo info, T original, T copy) where T : class
    {
        info.Members[original] = copy;
        return copy;
    }

    private TypeSymbol? Find(string name, TypeScope scope, int arity)
    {
        name = name[(name.LastIndexOf('.') + 1)..];

        if (arity == 0 && Pinned is not null && Pinned.TryGetValue(name, out var pinned)) return pinned;

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