using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Luft.Lexer;

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

        // Annotation prefix
        {
            TokenType.Annotation,
            [ new Regex(@"\G[@]", RegexOptions.Compiled) ]
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

    public static bool TryMatch(string text, int startIndex, out TokenType matchedType, out string matchedValue, out int length)
    {
        // Set default values
        matchedType = TokenType.Unknown;
        matchedValue = string.Empty;
        length = 0;

        // Go through all of the TokenTypes
        foreach (var type in Types)
        {
            if (!TokenRegexes.TryGetValue(type, out var regexes))
                continue;

            // Go Through all regexes for type
            foreach (var regex in regexes)
            {
                var match = regex.Match(text, startIndex);
                if (!match.Success)
                    continue;

                matchedType = type;
                if (type == TokenType.Identifier && Keywords.Contains(match.Value))
                    matchedType = TokenType.Keyword;

                matchedValue = match.Value;
                length = match.Length;
                return true;
            }
        }

        return false;
    }
}