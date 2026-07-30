using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Aero_Compiler.Lexer;

internal static class TokenMatcher
{
    /// <summary>
    /// Reserved Aero language keywords table.
    /// Identifiers are checked against this set for O(1) keyword classification.
    /// </summary>
    public static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "let", "var", "const", "fn", "func", "return", "if", "else",
        "while", "for", "in", "break", "continue", "struct", "class",
        "enum", "import", "export", "pub", "private", "true", "false",
        "null", "async", "await", "match", "type", "trait", "impl"
    };

    /// <summary>
    /// Regex token matchers anchored to current scanning position (\G).
    /// </summary>
    public static readonly Dictionary<TokenType, Regex[]> TokenRegexes = new()
    {
        // Keywords are matched as Identifiers first, then verified via the Keywords lookup set
        { TokenType.Keyword, [] }, 

        // Identifiers (Variable names, function names, types)
        { 
            TokenType.Identifier, 
            [ new Regex(@"\G[a-zA-Z_][a-zA-Z0-9_]*", RegexOptions.Compiled) ] 
        },

        // Float Literals (Standard decimals, floating suffixes, scientific notation e.g., 3.14, 1.0e-5, 2.5f)
        { 
            TokenType.FloatLiteral, 
            [ new Regex(@"\G\d+\.\d+(?:[eE][+-]?\d+)?f?|\G\d+[eE][+-]?\d+f?", RegexOptions.Compiled) ] 
        },

        // Integer Literals (Hexadecimal 0x, Binary 0b, and Decimal with optional '_' separators)
        { 
            TokenType.IntLiteral, 
            [ 
                new Regex(@"\G0[xX][0-9a-fA-F_]+", RegexOptions.Compiled),
                new Regex(@"\G0[bB][01_]+", RegexOptions.Compiled),
                new Regex(@"\G\d[0-9_]*", RegexOptions.Compiled)
            ] 
        },

        // Double-Quoted String Literals with escape character support (\", \n, \\, etc.)
        { 
            TokenType.StringLiteral, 
            [ new Regex(@"\G""([^""\\]|\\.)*""", RegexOptions.Compiled) ] 
        },

        // Single-Quoted Character Literals
        { 
            TokenType.CharLiteral, 
            [ new Regex(@"\G'([^'\\]|\\.)*'", RegexOptions.Compiled) ] 
        },

        // Operators: Multi-character operators precede single-character ones for greedy matching
        { 
            TokenType.Operator, 
            [ 
                new Regex(@"\G(<<=|>>=|==|!=|<=|>=|&&|\|\||\+\+|--|\+=|-=|\*=|/=|%=|&=|\|=|\^=|->|=>|\.\.|<<|>>|\+|-|\*|/|%|=|<|>|!|&|\||\^|~|\?|::)", RegexOptions.Compiled) 
            ] 
        },

        // Punctuation & Delimiters
        { 
            TokenType.Punctuation, 
            [ new Regex(@"\G[\(\)\{\}\[\],;\.:]", RegexOptions.Compiled) ] 
        },

        // Comments (Single-line // and Multi-line /* ... */)
        { 
            TokenType.Comment, 
            [ 
                new Regex(@"\G//.*", RegexOptions.Compiled), 
                new Regex(@"\G/\*[\s\S]*?\*/", RegexOptions.Compiled) 
            ] 
        },

        // Whitespaces (Spaces, Tabs, Newlines)
        { 
            TokenType.Whitespace, 
            [ new Regex(@"\G\s+", RegexOptions.Compiled) ] 
        },

        // End of File marker
        { 
            TokenType.Eof, 
            [ new Regex(@"\G\z", RegexOptions.Compiled) ] 
        }
    };
    
    // Cached once at startup; sorted by the numeric values assigned in TokenType enum
    private static readonly TokenType[] Types = Enum.GetValues<TokenType>();

    public static bool TryMatch(string text, int startIndex, SourceSpan currentSpan, out Token token, out int length)
    {
        token = new Token(TokenType.Unknown, "", SourceSpan.Unknown);
        length = 0;
        
        // Go through all possible TokenTypes in numeric order
        foreach (var type in Types)
        {
            if (!TokenRegexes.TryGetValue(type, out var regexes)) 
                continue;

            // Try each regex for the current TokenType
            foreach (var regex in regexes)
            {
                var match = regex.Match(text, startIndex);
                if (!match.Success) 
                    continue;
                
                // Reclassify identifiers as keywords if they match the reserved word set
                var actualType = type;
                if (type == TokenType.Identifier && Keywords.Contains(match.Value))
                {
                    actualType = TokenType.Keyword;
                }
                
                var endLocation = currentSpan.Start with { Column = currentSpan.Start.Column + match.Length };
                var tokenSpan = currentSpan with { End = endLocation };

                token = new Token(actualType, match.Value, tokenSpan);
                length = match.Length;
                return true;
            }
        }
        
        return false;
    }
}