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

    // Module level functions only, methods follow with user types in sub-task 6
    private void DeclareFunctions()
    {
        var symbols = new HashSet<string> { "main" };

        foreach (var module in _table.Modules.Values)
        {
            foreach (var f in module.Scope.Functions.Values.SelectMany(l => l))
            {
                if (f.GenericParameters.Count > 0) continue;

                var type = _types.LowerFunction(f.ReturnType, f.Parameters.Select(p => p.Type), module.Scope, f.Span);
                if (type is null) continue;

                var name = f.Access == AccessMod.Public ? ExportName(f, module.Scope) : InternalName(f);
                if (!symbols.Add(name))
                {
                    Error($"The symbol '{name}' is defined more than once.", f.Span);
                    continue;
                }

                var value = Module.AddFunction(name, type.Value);
                switch (f.Access)
                {
                    case AccessMod.Private: value.Linkage = LLVMLinkage.LLVMInternalLinkage; break;
                    case AccessMod.Public: break; // external, callable from any language
                    default: value.Visibility = LLVMVisibility.LLVMHiddenVisibility; break;
                }

                _functions[f] = new DeclaredFunction(value, type.Value, module.Scope);
            }
        }
    }

    // Module_Sub_Name, underscores are banned in Aero names so '_' never collides. Only overloads get a parameter suffix.
    private static string ExportName(FunctionSymbol f, TypeScope scope)
    {
        var prefix = f.Module.ModulePath.Length == 0 ? "" : f.Module.ModulePath.Replace('.', '_') + "_";
        var name = prefix + f.Name;

        if (!scope.Functions.TryGetValue(f.Name, out var overloads) || overloads.Count <= 1) return name;

        var suffix = string.Join("_", f.Parameters.Select(p => Sanitize(p.Type.ToString()!)));
        return suffix.Length == 0 ? name : $"{name}_{suffix}";
    }

    private static string Sanitize(string text) => new(text.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

    // Not exported, so the full signature keeps overloads and equal names in different modules apart
    private static string InternalName(FunctionSymbol f)
    {
        var prefix = f.Module.ModulePath.Length == 0 ? "" : f.Module.ModulePath + ".";
        return $"{prefix}{f.Name}({string.Join(",", f.Parameters.Select(p => p.Type))})";
    }

    // The C main calls Aero's Main and passes its Int result on as the exit code
    private bool EmitEntryPoint()
    {
        var all = _functions.Keys.Where(f => f.Name == "Main").ToList();
        if (all.Count > 1)
        {
            Error("There is more than one 'Main' function.", all[1].Span);
            return false;
        }
        if (all.Count == 1 && all[0].Parameters.Count > 0)
        {
            Error("'Main' with arguments is not supported yet, it needs arrays (sub-task 5).", all[0].Span);
            return false;
        }
        var mains = all;

        var mainType = LLVMTypeRef.CreateFunction(_context.Int32Type, []);
        var main = Module.AddFunction("main", mainType);

        _builder.PositionAtEnd(_context.AppendBasicBlock(main, "entry"));

        var exitCode = LLVMValueRef.CreateConstInt(_context.Int32Type, 0);
        if (mains.Count == 1)
        {
            var declared = _functions[mains[0]];
            var returnsVoid = mains[0].ReturnType == AeroType.Void;
            var call = _builder.BuildCall2(declared.Type, declared.Value, Array.Empty<LLVMValueRef>(), returnsVoid ? "" : "result");
            if (mains[0].ReturnType == AeroType.Int) exitCode = call;
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