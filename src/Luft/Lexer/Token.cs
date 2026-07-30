namespace Aero_Compiler.Lexer;

internal record Token(TokenType Type, string Value, SourceSpan Span);