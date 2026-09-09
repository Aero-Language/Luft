using Luft.Ast;
using Luft.Utility;

namespace Luft.TypeChecker.Symbols;

public abstract class TypeSymbol(string name, AccessMod access, ModuleSymbol module, TypeScope? containing, SourceSpan span) : ISignature
{
    public string Name { get; } = name;
    public AccessMod Access { get; } = access;
    public ModuleSymbol Module { get; } = module;
    public SourceSpan Span { get; } = span; // for spans/diagnostics, and LSP go-to-definition later

    public ValueList<GenericParameterType> GenericParameters { get; init; } = ValueList<GenericParameterType>.Empty;

    public TypeScope Scope { get; init; } = new TypeScope(containing);


    public SymbolSignature Signature => new(Name, GenericParameters, []);
}
