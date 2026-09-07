using Luft.TypeChecker;

namespace Luft.TypeChecker.Symbols;

public sealed class ModuleSymbol(string modulePath)
{
    public string ModulePath { get; } = modulePath;
    public TypeScope Scope { get; } = new();
}