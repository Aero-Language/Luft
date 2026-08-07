namespace Luft.Lexer;

public record Token(TokenType Type, string Value, SourceSpan Span);