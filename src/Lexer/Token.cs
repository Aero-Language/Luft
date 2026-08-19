using Luft.Utility;

namespace Luft.Lexer;

public record Token(TokenType Type, string Value, SourceSpan Span)
{
    public override string ToString() => $"{Type}: '{Value.ReplaceLineEndings()}'  –  at {Span}";
}