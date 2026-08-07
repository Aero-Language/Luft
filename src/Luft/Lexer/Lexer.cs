using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Luft.Lexer;

public class Lexer
{
    public Token[] Tokenize(string filePath)
    {
        if (!File.Exists(filePath)) 
            throw new FileNotFoundException(filePath);

        string code = File.ReadAllText(filePath);

        int line = 1;
        int column = 1;
        int cursor = 0;

        List<Token> tokens = new();

        // Only keep going if we are not past the actual lenght of the text
        while (cursor < code.Length)
        {
            var startLocation = new TextLocation(line, column);

            if (TokenMatcher.TryMatch(code, cursor, out var type, out var value, out int length))
            {
                // Advance line and column tracking based on the actual text matched
                UpdatePosition(value, ref line, ref column);
                
                var endLocation = new TextLocation(line, column);
                var tokenSpan = new SourceSpan(filePath, startLocation, endLocation);

                tokens.Add(new Token(type, value, tokenSpan));
                cursor += length;
            }
            else
            {
                // Unrecognized character fallback
                var charValue = code[cursor].ToString();
                column++;
                cursor++;

                var endLocation = new TextLocation(line, column);
                var tokenSpan = new SourceSpan(filePath, startLocation, endLocation);

                tokens.Add(new Token(TokenType.Unknown, charValue, tokenSpan));
            }
        }

        // Always append EOF token at the end
        if (tokens.Count == 0 || tokens[^1].Type != TokenType.Eof)
        {
            var eofLocation = new TextLocation(line, column);
            var eofSpan = new SourceSpan(filePath, eofLocation, eofLocation);
            tokens.Add(new Token(TokenType.Eof, string.Empty, eofSpan));
        }

        return tokens.ToArray();
    }

    private static void UpdatePosition(string text, ref int line, ref int column)
    {
        foreach (char ch in text)
        {
            if (ch == '\n')
            {
                line++;
                column = 1;
            }
            else if (ch != '\r') // Ignore carriage return for \r\n line endings
            {
                column++;
            }
        }
    }
}