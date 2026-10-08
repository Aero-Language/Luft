using LLVMSharp.Interop;

namespace Luft.Builder;

public static class LlvmBackend
{
    private static bool _initialized;

    public static string HostTriple => LLVMTargetRef.DefaultTriple;

    // Registers every target so cross-compiling only needs a different triple
    public static void Initialize()
    {
        if (_initialized) return;

        LLVM.InitializeAllTargetInfos();
        LLVM.InitializeAllTargets();
        LLVM.InitializeAllTargetMCs();
        LLVM.InitializeAllAsmPrinters();
        LLVM.InitializeAllAsmParsers();
        _initialized = true;
    }

    public static bool TryEmitObject(LLVMModuleRef module, string triple, string objectPath, out string error)
    {
        Initialize();

        if (!LLVMTargetRef.TryGetTargetFromTriple(triple, out var target, out error)) return false;

        // PIC so the object also links into shared libraries and PIE executables
        var machine = target.CreateTargetMachine(triple, "generic", "",
            LLVMCodeGenOptLevel.LLVMCodeGenLevelDefault,
            LLVMRelocMode.LLVMRelocPIC,
            LLVMCodeModel.LLVMCodeModelDefault);

        module.Target = triple;

        return machine.TryEmitToFile(module, objectPath, LLVMCodeGenFileType.LLVMObjectFile, out error);
    }
}