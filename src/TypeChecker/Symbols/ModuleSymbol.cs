using Luft.Utility;

namespace Luft.TypeChecker.Symbols;

public sealed class ModuleSymbol(string modulePath, SourceSpan span)
{
    public string ModulePath { get; } = modulePath;
    public SourceSpan Span { get; } = span;
    public TypeScope Scope { get; init; }
}