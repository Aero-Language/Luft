namespace Aero_Compiler.Lexer;

internal static class Lexer
{
    public static Token[] Tokenize(string filePath)
    {
        if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);
        
        string code = File.ReadAllText(filePath);
        
        int line = 1;
        int column = 1;
        int cursor = 0;

        var currentSpan = SourceSpan.Unknown;
        List<Token> tokens = [];

        while (tokens.LastOrDefault() != null && tokens.Last().Type is not TokenType.Eof)
        {
            if (TokenMatcher.TryMatch(code, cursor, currentSpan, out var token, out int length))
            {
                tokens.Add(token);
                currentSpan = token.Span;
                cursor += length;
            }
        }
        
        return tokens.ToArray();
    }
}