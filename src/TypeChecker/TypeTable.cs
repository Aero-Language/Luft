using Luft.Ast.Nodes;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.TypeChecker;

public sealed class TypeTable
{
    public static readonly ValueList<AeroType> PrimitiveTypes = 
    [
        AeroType.Byte,
        AeroType.Char,
        AeroType.String,
        AeroType.Bool,
        AeroType.Int,
        AeroType.Float,
        AeroType.Void
    ];
    
    public Dictionary<string, ModuleSymbol> Modules { get; } = new();
    public Dictionary<string, List<ImportStatementNode>> ImportsByFile { get; } = new();

    public ModuleSymbol GetOrAddModule(string modulePath, SourceSpan span)
    {
        if (!Modules.TryGetValue(modulePath, out var module))
            Modules[modulePath] = module = new ModuleSymbol(modulePath, span);
        return module;
    }
}