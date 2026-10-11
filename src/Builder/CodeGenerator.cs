using LLVMSharp.Interop;
using Luft.Ast;
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

        var failed = false;
        foreach (var (symbol, declared) in _functions)
        {
            using var emitter = new FunctionEmitter(_context, Module, _types, _table.Typed, _functions, _strings)
            {
                Diagnostics = Diagnostics,
                OnDiagnostic = OnDiagnostic,
                OnError = OnError
            };
            emitter.Emit(symbol, declared);
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

        foreach (var module in _table.Modules.Values) DeclareScope(module.Scope, null, "", true, symbols);
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

        foreach (var symbol in scope.Types.Values.SelectMany(l => l))
        {
            if (symbol is not (ClassSymbol or StructSymbol or RecordSymbol) || symbol.GenericParameters.Count > 0) continue;

            var nested = path.Length == 0 ? symbol.Name : $"{path}.{symbol.Name}";
            DeclareScope(symbol.Scope, symbol, nested, exported && symbol.Access == AccessMod.Public, symbols);
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