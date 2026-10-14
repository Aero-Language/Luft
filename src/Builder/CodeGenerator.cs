using LLVMSharp.Interop;
using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

public sealed class CodeGenerator : AeroThrower, IDisposable
{
    protected override CompilerStage Stage => CompilerStage.CodeGen;

    private readonly LLVMContextRef _context;
    private readonly LLVMBuilderRef _builder;
    private readonly TypeTable _table;
    private readonly Dictionary<FunctionSymbol, DeclaredFunction> _functions = new();
    private readonly Dictionary<ConstructorKey, DeclaredFunction> _constructors = new();
    private readonly MemberTable _members = new();
    private LLVMValueRef _staticInit; // unset when there is nothing to initialize
    private ModuleSymbol? _declaringModule;
    private readonly List<(ClassSymbol Type, string Path, ModuleSymbol Module)> _classes = new();
    private readonly Dictionary<string, LLVMValueRef> _strings = new();
    private TypeLowering _types = null!;

    public LLVMModuleRef Module { get; }
    public TypeLowering Types => _types;

    // True when the module calls into the C runtime, which then has to be linked in
    public bool UsesRuntime
    {
        get
        {
            for (var f = Module.FirstFunction; f.Handle != IntPtr.Zero; f = f.NextFunction)
                if (f.Name.StartsWith("aero_")) return true;
            return false;
        }
    }

    public CodeGenerator(string moduleName, TypeTable table)
    {
        LlvmBackend.Initialize();

        _table = table;
        _context = LLVMContextRef.Create();
        Module = _context.CreateModuleWithName(moduleName);
        _builder = _context.CreateBuilder();
    }

    public bool Generate()
    {
        // Created here because Diagnostics is assigned after construction
        _types = new TypeLowering(_context, _table) { Diagnostics = Diagnostics, OnDiagnostic = OnDiagnostic, OnError = OnError };

        foreach (var module in _table.Modules.Values) LowerScope(module.Scope);
        if (_types.HasErrors) return false;

        DeclareFunctions();
        if (_types.HasErrors) return false;

        DeclareVTables();

        var failed = false;
        foreach (var (symbol, declared) in _functions)
        {
            using var emitter = CreateEmitter();
            emitter.Emit(symbol, declared);
            failed |= emitter.HasErrors;
        }
        foreach (var (key, declared) in _constructors)
        {
            using var emitter = CreateEmitter();
            emitter.EmitConstructor(key, declared);
            failed |= emitter.HasErrors;
        }
        foreach (var (key, declared) in _members.Accessors)
        {
            using var emitter = CreateEmitter();
            emitter.EmitAccessor(key, declared);
            failed |= emitter.HasErrors;
        }
        foreach (var (symbol, declared) in _members.Operators)
        {
            using var emitter = CreateEmitter();
            emitter.EmitOperator(symbol, declared);
            failed |= emitter.HasErrors;
        }
        foreach (var (type, declared) in _members.Destroyers)
        {
            using var emitter = CreateEmitter();
            emitter.EmitDestroyer((ClassSymbol)type, declared);
            failed |= emitter.HasErrors;
        }
        if (_members.Initializers.Count > 0)
        {
            _staticInit = Module.AddFunction("aero.init", LLVMTypeRef.CreateFunction(_context.VoidType, [], false));
            _staticInit.Linkage = LLVMLinkage.LLVMInternalLinkage;

            using var emitter = CreateEmitter();
            emitter.EmitStaticInitializers(_staticInit, _members.Initializers);
            failed |= emitter.HasErrors;
        }
        if (failed || _types.HasErrors) return false;

        if (!EmitEntryPoint()) return false;

        if (!Module.TryVerify(LLVMVerifierFailureAction.LLVMReturnStatusAction, out var message))
        {
            Error($"Generated invalid LLVM IR: {message}", SourceSpan.Unknown);
            return false;
        }

#if DEBUG
        Console.Error.WriteLine(Module.PrintToString());
#endif
        return true;
    }

    private FunctionEmitter CreateEmitter() => new(_context, Module, _types, _table.Typed, _functions, _constructors, _members, _strings)
    {
        Diagnostics = Diagnostics,
        OnDiagnostic = OnDiagnostic,
        OnError = OnError
    };

    // Lowers every non generic declaration once, so type problems show up before function bodies exist
    private void LowerScope(TypeScope scope)
    {
        // Generic declarations are lowered per instantiation, extension functions with sub-task 7
        if (!scope.AllGenerics().Any())
        {
            foreach (var f in scope.Functions.Values.SelectMany(l => l).Where(f => f.GenericParameters.Count == 0))
                _types.LowerFunction(f.ReturnType, f.Parameters.Select(p => p.Type), scope, f.Span);

            foreach (var o in scope.Operators.Values.SelectMany(l => l))
                _types.LowerFunction(o.ReturnType, o.Parameters.Select(p => p.Type), scope, o.Span);

            foreach (var c in scope.Constructors)
                _types.LowerFunction(AeroType.Void, c.Parameters.Select(p => p.Type), scope, c.Span);
        }

        foreach (var symbol in scope.Types.Values.SelectMany(l => l))
        {
            if (symbol.GenericParameters.Count > 0 || symbol is AnnotationSymbol) continue;

            if (symbol is ClassSymbol or StructSymbol or RecordSymbol) _types.LayoutOf(symbol, symbol.Span);
            LowerScope(symbol.Scope);
        }
    }

    private void DeclareFunctions()
    {
        var symbols = new HashSet<string> { "main" };

        foreach (var module in _table.Modules.Values)
        {
            _declaringModule = module;
            DeclareScope(module.Scope, null, "", true, symbols);
        }
    }

    // The functions of one scope, then the methods of every non generic type in it. `path` is the dotted type path, empty at module level.
    private void DeclareScope(TypeScope scope, TypeSymbol? owner, string path, bool exported, HashSet<string> symbols)
    {
        foreach (var f in scope.Functions.Values.SelectMany(l => l))
        {
            if (f.GenericParameters.Count > 0) continue;

            var hasSelf = owner is not null && !f.MemberMods.HasFlag(MemberMod.Static);
            var type = _types.LowerFunction(f.ReturnType, f.Parameters.Select(p => p.Type), scope, f.Span);
            if (type is null) continue;

            // self comes first and is always a pointer, to the object for classes and to the value for structs
            if (hasSelf) type = LLVMTypeRef.CreateFunction(type.Value.ReturnType, new[] { _types.Ptr }.Concat(type.Value.ParamTypes).ToArray(), false);

            var external = f.Access == AccessMod.Public && exported;
            var name = external ? ExportName(f, scope, path) : InternalName(f, path);
            if (!symbols.Add(name))
            {
                Error($"The symbol '{name}' is defined more than once.", f.Span);
                continue;
            }

            var value = Module.AddFunction(name, type.Value);
            if (f.Access == AccessMod.Private) value.Linkage = LLVMLinkage.LLVMInternalLinkage;
            else if (!external) value.Visibility = LLVMVisibility.LLVMHiddenVisibility;
            // external otherwise, callable from any language

            _functions[f] = new DeclaredFunction(value, type.Value, scope, owner, hasSelf);
        }

        DeclareExtensionFunctions(scope, exported, symbols);
        DeclareExtensionProperties(scope, exported, symbols);
        DeclareFields(scope, owner, path, exported, symbols);
        DeclareProperties(scope, owner, path, exported, symbols);
        if (owner is not null) DeclareOperators(scope, owner, path, exported, symbols);

        foreach (var symbol in scope.Types.Values.SelectMany(l => l))
        {
            if (symbol is not (ClassSymbol or StructSymbol or RecordSymbol) || symbol.GenericParameters.Count > 0) continue;

            var nested = path.Length == 0 ? symbol.Name : $"{path}.{symbol.Name}";
            var nestedExported = exported && symbol.Access == AccessMod.Public;

            if (symbol is ClassSymbol classSymbol) _classes.Add((classSymbol, nested, _declaringModule!));

            DeclareConstructors(symbol, nested, nestedExported, symbols);
            DeclareScope(symbol.Scope, symbol, nested, nestedExported, symbols);
        }
    }

    // Every class gets a destroy function (its destructor, then its base class's) and a vtable: that function followed by one entry per virtual slot
    private void DeclareVTables()
    {
        var destroyType = LLVMTypeRef.CreateFunction(_context.VoidType, [_types.Ptr], false);

        foreach (var (type, path, module) in _classes)
        {
            var destroy = Module.AddFunction($"{QualifiedName(module, path)}.<destroy>", destroyType);
            destroy.Linkage = LLVMLinkage.LLVMInternalLinkage;

            _members.Destroyers[type] = new DeclaredFunction(destroy, destroyType, type.Scope, type, true);
        }

        foreach (var (type, path, module) in _classes)
        {
            var entries = new List<LLVMValueRef> { _members.Destroyers[type].Value };

            // A slot without an implementation (abstract) stays null
            foreach (var slot in VirtualTables.Of(type, _types, _members.VirtualSlots))
                entries.Add(slot.Implementation.Body is not null && _functions.TryGetValue(slot.Implementation, out var implementation)
                    ? implementation.Value
                    : LLVMValueRef.CreateConstPointerNull(_types.Ptr));

            var array = LLVMValueRef.CreateConstArray(_types.Ptr, entries.ToArray());
            var table = Module.AddGlobal(array.TypeOf, $"{QualifiedName(module, path)}.<vtable>");
            table.Initializer = array;
            table.IsGlobalConstant = true;
            table.Linkage = LLVMLinkage.LLVMInternalLinkage;

            _members.VTables[type] = table;
        }
    }

    private static string QualifiedName(ModuleSymbol module, string path)
        => (module.ModulePath.Length == 0 ? "" : module.ModulePath + ".") + path;

    // An operator is a method named after its Operator: self is the left operand, the parameter the right one, no parameter means unary; a static one has no self
    private void DeclareOperators(TypeScope scope, TypeSymbol owner, string path, bool exported, HashSet<string> symbols)
    {
        foreach (var o in scope.Operators.Values.SelectMany(l => l))
        {
            if (o.Body is null) continue;

            var type = _types.LowerFunction(o.ReturnType, o.Parameters.Select(p => p.Type), scope, o.Span);
            if (type is null) continue;

            if (!o.IsStatic) type = LLVMTypeRef.CreateFunction(type.Value.ReturnType, new[] { _types.Ptr }.Concat(type.Value.ParamTypes).ToArray(), false);

            var external = o.Access == AccessMod.Public && exported;
            var suffix = string.Join("_", o.Parameters.Select(p => Sanitize(p.Type.ToString()!)));
            var name = external
                ? GlobalName($"Operator{o.Op}{(suffix.Length == 0 ? "" : "_" + suffix)}", path, true)
                : GlobalName($"<operator>{o.Op}({string.Join(",", o.Parameters.Select(p => p.Type))})", path, false);

            if (!symbols.Add(name))
            {
                Error($"The symbol '{name}' is defined more than once.", o.Span);
                continue;
            }

            var value = Module.AddFunction(name, type.Value);
            if (o.Access == AccessMod.Private) value.Linkage = LLVMLinkage.LLVMInternalLinkage;
            else if (!external) value.Visibility = LLVMVisibility.LLVMHiddenVisibility;

            _members.Operators[o] = new DeclaredFunction(value, type.Value, scope, owner, !o.IsStatic);
        }
    }

    // Static, const and module level fields live in globals, their initial values are stored at startup
    private void DeclareFields(TypeScope scope, TypeSymbol? owner, string path, bool exported, HashSet<string> symbols)
    {
        foreach (var field in scope.Fields.Values.SelectMany(l => l))
        {
            var isGlobal = owner is null || field.Declaration.MemberMods.HasFlag(MemberMod.Static) || field.VarKind == VariableKind.Const;
            if (!isGlobal) continue;

            var type = field.Type;
            if (type.IsAuto && field.Initializer is not null && _table.Typed.TypeOf(field.Initializer) is { } inferred) type = inferred;
            if (!_types.TryLower(type, scope, field.Span, out var lowered)) continue;

            var external = field.Access == AccessMod.Public && exported;
            var name = GlobalName(field.Name, path, external);
            if (!symbols.Add(name))
            {
                Error($"The symbol '{name}' is defined more than once.", field.Span);
                continue;
            }

            var variable = AddGlobal(name, lowered, type, external);
            _members.Fields[field] = variable;

            if (field.Initializer is not null) _members.Initializers.Add(new StaticInitializer(variable, field.Initializer, scope, field.Span));
        }
    }

    // Instance properties are laid out with their type, static and module level ones get a global backing value and accessor functions
    private void DeclareProperties(TypeScope scope, TypeSymbol? owner, string path, bool exported, HashSet<string> symbols)
    {
        foreach (var p in scope.Properties.Values.SelectMany(l => l))
        {
            var isGlobal = owner is null || p.Declaration.MemberMods.HasFlag(MemberMod.Static);
            var external = p.Access == AccessMod.Public && exported;

            if (isGlobal)
            {
                _members.GlobalProperties.Add(p);

                if (_types.NeedsBacking(p) && _types.TryLower(p.Type, scope, p.Span, out var lowered))
                {
                    var name = GlobalName($"<backing>{p.Name}", path, false);
                    if (!symbols.Add(name))
                    {
                        Error($"The symbol '{name}' is defined more than once.", p.Span);
                        continue;
                    }

                    var variable = AddGlobal(name, lowered, p.Type, false);
                    _members.Backing[p] = variable;

                    if (p.Initializer is not null) _members.Initializers.Add(new StaticInitializer(variable, p.Initializer, scope, p.Span));
                }
            }

            DeclareAccessor(p, false, scope, owner, path, !isGlobal, external, symbols);
            DeclareAccessor(p, true, scope, owner, path, !isGlobal, external, symbols);
        }
    }

    // Only accessors with a body are functions, an automatic one reads or writes the backing value directly
    private void DeclareAccessor(PropertySymbol p, bool isSetter, TypeScope scope, TypeSymbol? owner, string path, bool hasSelf, bool external, HashSet<string> symbols, AeroType? receiver = null)
    {
        var accessor = isSetter ? p.Setter : p.Getter;
        if (accessor?.Body is null) return;

        var type = _types.LowerFunction(isSetter ? AeroType.Void : p.Type, isSetter ? new[] { p.Type } : Array.Empty<AeroType>(), scope, p.Span);
        if (type is null) return;

        // self comes first, as for every method
        if (hasSelf) type = LLVMTypeRef.CreateFunction(type.Value.ReturnType, new[] { _types.Ptr }.Concat(type.Value.ParamTypes).ToArray(), false);

        var memberName = receiver is null ? p.Name : $"{Sanitize(receiver.ToString()!)}_{p.Name.Split('.')[^1]}";
        var name = GlobalName($"{(isSetter ? "set" : "get")}_{memberName}", path, external);
        if (!symbols.Add(name))
        {
            Error($"The symbol '{name}' is defined more than once.", p.Span);
            return;
        }

        var value = Module.AddFunction(name, type.Value);
        if (p.Access == AccessMod.Private) value.Linkage = LLVMLinkage.LLVMInternalLinkage;
        else if (!external) value.Visibility = LLVMVisibility.LLVMHiddenVisibility;

        _members.Accessors[new AccessorKey(p, isSetter)] = new DeclaredFunction(value, type.Value, scope, owner, hasSelf, receiver);
    }

    // Module_Type_Name when exported, a dotted name otherwise; the declaring module is the one DeclareFunctions is walking
    private string GlobalName(string name, string path, bool external)
    {
        var module = _declaringModule?.ModulePath ?? "";
        if (external)
            return (module.Length == 0 ? "" : module.Replace('.', '_') + "_") + (path.Length == 0 ? "" : path.Replace('.', '_') + "_") + name;

        return (module.Length == 0 ? "" : module + ".") + (path.Length == 0 ? "" : path + ".") + name;
    }

    // Zero until the startup initializer stores the real value
    private GlobalVariable AddGlobal(string name, LLVMTypeRef type, AeroType aeroType, bool external)
    {
        var global = Module.AddGlobal(type, name);
        global.Initializer = LLVMValueRef.CreateConstNull(type);
        if (!external) global.Linkage = LLVMLinkage.LLVMInternalLinkage;
        return new GlobalVariable(global, type, aeroType);
    }

    // One init function per constructor: it takes the allocated object (self) first and fills it in, the caller allocates
    private void DeclareConstructors(TypeSymbol type, string path, bool exported, HashSet<string> symbols)
    {
        var layout = _types.LayoutOf(type, type.Span);
        if (layout is null) return;

        var modulePrefix = type.Module.ModulePath.Length == 0 ? "" : type.Module.ModulePath;

        foreach (var (key, parameters) in ConstructorShapes.Of(type, layout))
        {
            var lowered = _types.LowerFunction(AeroType.Void, parameters.Select(p => p.Type), type.Scope, type.Span);
            if (lowered is null) continue;

            var functionType = LLVMTypeRef.CreateFunction(lowered.Value.ReturnType, new[] { _types.Ptr }.Concat(lowered.Value.ParamTypes).ToArray(), false);

            var external = exported && (key.Symbol is null || key.Symbol.Access == AccessMod.Public);
            var suffix = string.Join("_", parameters.Select(p => Sanitize(p.Type.ToString()!)));
            var name = external
                ? $"{(modulePrefix.Length == 0 ? "" : modulePrefix.Replace('.', '_') + "_")}{path.Replace('.', '_')}_New{(suffix.Length == 0 ? "" : "_" + suffix)}"
                : $"{(modulePrefix.Length == 0 ? "" : modulePrefix + ".")}{path}.<init:{key.Kind}>({string.Join(",", parameters.Select(p => p.Type))})";

            if (!symbols.Add(name))
            {
                Error($"The symbol '{name}' is defined more than once.", type.Span);
                continue;
            }

            var value = Module.AddFunction(name, functionType);
            if (key.Symbol is { Access: AccessMod.Private }) value.Linkage = LLVMLinkage.LLVMInternalLinkage;
            else if (!external) value.Visibility = LLVMVisibility.LLVMHiddenVisibility;

            _constructors[key] = new DeclaredFunction(value, functionType, type.Scope, type, true);
        }
    }

    // Module_Sub_Type_Name, underscores are banned in Aero names so '_' never collides. Only overloads get a parameter suffix.
    private static string ExportName(FunctionSymbol f, TypeScope scope, string path)
    {
        var prefix = f.Module.ModulePath.Length == 0 ? "" : f.Module.ModulePath.Replace('.', '_') + "_";
        if (path.Length > 0) prefix += path.Replace('.', '_') + "_";
        var name = prefix + f.Name;

        if (!scope.Functions.TryGetValue(f.Name, out var overloads) || overloads.Count <= 1) return name;

        var suffix = string.Join("_", f.Parameters.Select(p => Sanitize(p.Type.ToString()!)));
        return suffix.Length == 0 ? name : $"{name}_{suffix}";
    }

    // An extension is a function whose first parameter is a pointer to the receiver (the object itself for a class).
    // Generic ones and ones with a generic target wait for the generics work.
    private void DeclareExtensionFunctions(TypeScope scope, bool exported, HashSet<string> symbols)
    {
        foreach (var f in scope.ExtensionFunctions.Values.SelectMany(l => l))
        {
            if (f.GenericParameters.Count > 0 || f.ExtensionTarget is not { } target || target is GenericType || ContainsGeneric(target, scope)) continue;

            var type = _types.LowerFunction(f.ReturnType, f.Parameters.Select(p => p.Type), scope, f.Span);
            if (type is null) continue;

            type = LLVMTypeRef.CreateFunction(type.Value.ReturnType, new[] { _types.Ptr }.Concat(type.Value.ParamTypes).ToArray(), false);

            var external = f.Access == AccessMod.Public && exported;
            var name = external ? ExportExtensionName(f, target, scope) : InternalName(f, "");
            if (!symbols.Add(name))
            {
                Error($"The symbol '{name}' is defined more than once.", f.Span);
                continue;
            }

            var value = Module.AddFunction(name, type.Value);
            if (f.Access == AccessMod.Private) value.Linkage = LLVMLinkage.LLVMInternalLinkage;
            else if (!external) value.Visibility = LLVMVisibility.LLVMHiddenVisibility;

            var owner = _types.SymbolOf(target, scope) is ClassSymbol or StructSymbol or RecordSymbol ? _types.SymbolOf(target, scope) : null;
            _functions[f] = new DeclaredFunction(value, type.Value, scope, owner, true, target);
        }
    }

    // Extension properties have no storage, they are a getter and a setter taking the receiver like an extension function
    private void DeclareExtensionProperties(TypeScope scope, bool exported, HashSet<string> symbols)
    {
        foreach (var p in scope.ExtensionProperties.Values.SelectMany(l => l))
        {
            if (p.ExtensionTarget is not { } target || target is GenericType || ContainsGeneric(target, scope)) continue;

            var resolved = _types.SymbolOf(target, scope);
            var owner = resolved is ClassSymbol or StructSymbol or RecordSymbol ? resolved : null;
            var external = p.Access == AccessMod.Public && exported;

            DeclareAccessor(p, false, scope, owner, "", true, external, symbols, target);
            DeclareAccessor(p, true, scope, owner, "", true, external, symbols, target);
        }
    }

    private static bool ContainsGeneric(AeroType type, TypeScope scope)
    {
        var generics = scope.AllGenerics().ToHashSet();
        return type is ScalarType s && generics.Contains(s.Name) || type is ArrayType a && ContainsGeneric(a.ElementType, scope);
    }

    // Module_Target_Name, a different target keeps equal member names apart and only overloads get a parameter suffix
    private static string ExportExtensionName(FunctionSymbol f, AeroType target, TypeScope scope)
    {
        var prefix = f.Module.ModulePath.Length == 0 ? "" : f.Module.ModulePath.Replace('.', '_') + "_";
        var name = $"{prefix}{Sanitize(target.ToString()!)}_{f.Name.Split('.')[^1]}";

        if (!scope.ExtensionFunctions.TryGetValue(f.Name, out var overloads) || overloads.Count <= 1) return name;

        var suffix = string.Join("_", f.Parameters.Select(p => Sanitize(p.Type.ToString()!)));
        return suffix.Length == 0 ? name : $"{name}_{suffix}";
    }

    private static string Sanitize(string text) => new(text.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    // Not exported, so the full signature keeps overloads and equal names in different modules apart
    private static string InternalName(FunctionSymbol f, string path)
    {
        var prefix = f.Module.ModulePath.Length == 0 ? "" : f.Module.ModulePath + ".";
        if (path.Length > 0) prefix += path + ".";
        return $"{prefix}{f.Name}({string.Join(",", f.Parameters.Select(p => p.Type))})";
    }

    // The C main calls Aero's Main and passes its Int result on as the exit code
    private bool EmitEntryPoint()
    {
        var mains = _functions.Where(kv => kv.Value.Owner is null && kv.Key.Name == "Main").Select(kv => kv.Key).ToList();
        if (mains.Count > 1)
        {
            Error("There is more than one 'Main' function.", mains[1].Span);
            return false;
        }

        var main0 = mains.Count == 1 ? mains[0] : null;
        var takesArgs = main0 is { Parameters.Count: 1 } && main0.Parameters[0].Type is ArrayType { ElementType: var element } && element == AeroType.String;
        if (main0 is not null && main0.Parameters.Count > 0 && !takesArgs)
        {
            Error("'Main' can only take a single 'String[]' parameter.", main0.Span);
            return false;
        }

        var mainType = takesArgs
            ? LLVMTypeRef.CreateFunction(_context.Int32Type, [_context.Int32Type, _types.Ptr])
            : LLVMTypeRef.CreateFunction(_context.Int32Type, []);
        var main = Module.AddFunction("main", mainType);

        _builder.PositionAtEnd(_context.AppendBasicBlock(main, "entry"));

        // Statics are ready before any user code runs
        if (_staticInit.Handle != IntPtr.Zero)
            _builder.BuildCall2(LLVMTypeRef.CreateFunction(_context.VoidType, [], false), _staticInit, [], "");

        var exitCode = LLVMValueRef.CreateConstInt(_context.Int32Type, 0);
        if (main0 is not null)
        {
            var declared = _functions[main0];
            var returnsVoid = main0.ReturnType == AeroType.Void;
            var arguments = Array.Empty<LLVMValueRef>();

            if (takesArgs)
            {
                // The runtime turns argv (without the program name) into a UTF-16 String[]
                var slot = _builder.BuildAlloca(_types.ArrayStruct, "args");
                var convertType = LLVMTypeRef.CreateFunction(_context.VoidType, [_types.Ptr, _context.Int32Type, _types.Ptr]);
                var convert = Module.AddFunction("aero_args", convertType);
                _builder.BuildCall2(convertType, convert, [slot, main.GetParam(0), main.GetParam(1)]);
                arguments = [_builder.BuildLoad2(_types.ArrayStruct, slot, "args")];
            }

            var call = _builder.BuildCall2(declared.Type, declared.Value, arguments, returnsVoid ? "" : "result");
            if (main0.ReturnType == AeroType.Int) exitCode = call;
        }

        _builder.BuildRet(exitCode);
        return true;
    }

    public void Dispose()
    {
        _builder.Dispose();
        Module.Dispose();
        _context.Dispose();
    }
}