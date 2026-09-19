using Luft.Utility;

namespace Luft.TypeChecker.Symbols;

public abstract class SourceSymbol(SourceSpan span)
{
    public SourceSpan Span { get; } = span;
}