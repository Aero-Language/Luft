using LLVMSharp.Interop;
using Luft.Utility;

namespace Luft.Builder;

public sealed class CodeGenerator : AeroThrower, IDisposable
{
    protected override CompilerStage Stage => CompilerStage.CodeGen;

    private readonly LLVMContextRef _context;
    private readonly LLVMBuilderRef _builder;

    public LLVMModuleRef Module { get; }

    public CodeGenerator(string moduleName)
    {
        LlvmBackend.Initialize();

        _context = LLVMContextRef.Create();
        Module = _context.CreateModuleWithName(moduleName);
        _builder = _context.CreateBuilder();
    }

    // Skeleton: only the C entry point. Aero functions are lowered from sub-task 4 on.
    public bool Generate()
    {
        EmitEntryPoint();

        if (!Module.TryVerify(LLVMVerifierFailureAction.LLVMReturnStatusAction, out var message))
        {
            Error($"Generated invalid LLVM IR: {message}", SourceSpan.Unknown);
            return false;
        }

        return true;
    }

    private void EmitEntryPoint()
    {
        var mainType = LLVMTypeRef.CreateFunction(_context.Int32Type, []);
        var main = Module.AddFunction("main", mainType);

        _builder.PositionAtEnd(_context.AppendBasicBlock(main, "entry"));
        _builder.BuildRet(LLVMValueRef.CreateConstInt(_context.Int32Type, 0));
    }

    public void Dispose()
    {
        _builder.Dispose();
        Module.Dispose();
        _context.Dispose();
    }
}