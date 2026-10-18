using Luft.Utility;

namespace Luft.TypeChecker.Symbols;

public sealed class ModuleSymbol(string modulePath, SourceSpan span)
{
    public string ModulePath { get; } = modulePath;
    public SourceSpan Span { get; } = span;
    public TypeScope Scope { get; init; }

    // The package id of the library that declares the module, null for the project's own modules
    public string? LibraryId { get; init; }
    public bool IsLibrary => LibraryId is not null;
}