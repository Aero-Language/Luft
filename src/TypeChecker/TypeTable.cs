using Luft.TypeChecker.Symbols;

namespace Luft.TypeChecker;

public sealed class TypeTable
{
    public Dictionary<string, ModuleSymbol> Modules { get; } = new();

    public ModuleSymbol GetOrAddModule(string modulePath)
    {
        if (!Modules.TryGetValue(modulePath, out var module))
            Modules[modulePath] = module = new ModuleSymbol(modulePath);
        return module;
    }
}