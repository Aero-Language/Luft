using System.Runtime.CompilerServices;
using Luft.Utility;

namespace Luft.Lexer;

public sealed class Tokenizer : SafeIterator<char>
{
    private string FilePath { get; set; } = string.Empty;
    private int Line { get; set; } = 1;
    private int Column { get; set; } = 1;

    private List<Token> Tokens { get; set; } = [];

    public Tokenizer()
    {
        PopTask = amount =>
        {
            var skipped = Items[Index..(Index + amount)];

            foreach (var c in skipped)
            {
                switch (c)
                {
                    case '\n': Line++; Column = 1; break;
                    case '\r': Column = 1; break;
                    default: Column++; break;
                }
            }
        };
    }
    
    public Token[] Tokenize(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        FilePath = filePath;
        var source = File.ReadAllText(filePath).ToArray();
        Start(source);
        
        Tokens = new List<Token>(source.Length / 5);
        

        while (Index < source.Length)
        {
            char c = Peek();

            LexPass(c);
        }

        var fileSpan = new SourceSpan(filePath, TextLocation.Zero, new TextLocation(source.Count('\n') + 1, source.Length - source.LastIndexOf('\n') - 1));
        Tokens.Add(new Token(TokenType.Eof, "", fileSpan));
        return Tokens.ToArray();
    }
    
    void LexPass(char c)
    {
        // Whitespace
        if (char.IsWhiteSpace(c))
        {
            PopWhitespace();
            return;
        }

        // Comments or Division Operators
        if (c == '/')
        {
            if (Peek(1) == '/' || Peek(1) == '*')
            {
                PopComment();
                return;
            }
        }

        // Number Literals (Float vs Int)
        if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(Peek(1))))
        {
            PopNumber();
            return;
        }

        // Interpolated strings
        if (c is '$')
        {
            PopInterpolatedString();
            return;
        }
        
        // String & Character Literals
        if (c is '"' or '\'')
        {
            PopStringOrChar(c);
            return;
        }

        // Identifiers & Keywords
        if (char.IsLetter(c) || c == '_')
        {
            PopIdentifierOrKeyword();
            return;
        }

        // Operators & Punctuation
        var opToken = PopOperatorOrPunctuation();
        if (opToken.Type != TokenType.Unknown)
        {
            Tokens.Add(opToken);
            return;
        }

        // Unknown / Fallback
        var span = new SourceSpan(FilePath, new TextLocation(Line, Column), new TextLocation(Line, Column + 1));
        Tokens.Add(new Token(TokenType.Unknown, Peek().ToString(), span));
        Pop();
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ReadOnlySpan<char> PeekRange(int start, int target) => (uint)target < (uint)Items.Length ? Items[start..target] : "\0";

    bool IsMatch(string text)
    {
        if (Index + text.Length > Items.Length)
            return false;

        return Items.SequenceEqual(text);
    }
    void PopWhitespace()
    {
        var initialLoc = new TextLocation(Line, Column);
        
        int start = Index;
        char c = Peek();
        while (Index < Items.Length && char.IsWhiteSpace(c))
        {
            Pop();
            c = Peek();
        }
        var span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
        Tokens.Add(new Token(TokenType.Whitespace, PeekRange(start, Index).ToString(), span));
    }
    void PopComment()
    {
        var initialLoc = new TextLocation(Line, Column);
        
        int start = Index;
        if (Peek(1) == '/') // Line comment
        {
            Pop(2);
            while (Index < Items.Length && Peek() != '\n' && Peek() != '\r')
            {
                Pop();
            }
        }
        else if (Peek(1) == '*') // Block comment
        {
            Pop(2);
            while (Index < Items.Length)
            {
                if (Peek() == '*' && Peek(1) == '/')
                {
                    Pop(2);
                    break;
                }
                Pop();
            }
        }
        var span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
        Tokens.Add(new Token(TokenType.Comment, PeekRange(start, Index).ToString(), span));
    }
    void PopNumber()
    {
        var initialLoc = new TextLocation(Line, Column);
        
        int start = Index;
        bool isFloat = false;
        bool isHex = Peek() is '0' && Peek(1) is 'x';
        bool isBin = Peek() is '0' && Peek(1) is 'b';
        if (isHex || isBin) Pop(2);

        while (Index < Items.Length)
        {
            char current = Peek();
            
            if (char.IsAsciiDigit(current) && !isBin && !isHex) // handle int exclusively
            {
                Pop();
            }
            else if (current == '.' && !isFloat && char.IsAsciiDigit(Peek(1))) // handle float
            {
                isFloat = true;
                Pop();
            }
            else if (isHex && Extensions.HexChars.Contains(Peek())) // handle hexadecimal
            {
                Pop();
            }
            else if (isBin && Extensions.BinChars.Contains(Peek())) // handle binary
            {
                Pop();
            }
            else
            {
                break;
            }
        }

        TokenType type = isFloat ? TokenType.FloatLiteral : TokenType.IntLiteral;
        var span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
        Tokens.Add(new Token(type, PeekRange(start, Index).ToString(), span));
    }
    void PopInterpolatedString()
    {
        var startLoc = new TextLocation(Line, Column);

        // Emit InterpolationStart for $"
        Tokens.Add(new Token(TokenType.InterpolationStart, "$\"", new SourceSpan(FilePath, startLoc, new TextLocation(Line, Column + 2))));
        Pop(2); // Pop $"

        int start = Index;
        var currentLoc = new TextLocation(Line, Column);

        while (Index < Items.Length)
        {
            char c = Peek();

            if (c == '{')
            {
                // Emit literal string preceding the interpolated expression (if any)
                if (Index > start)
                {
                    var span = new SourceSpan(FilePath, currentLoc, new TextLocation(Line, Column));
                    Tokens.Add(new Token(TokenType.StringLiteral, new string(Items[start..Index]), span));
                }

                Pop(); // Pop '{'
                
                // Lex Tokens inside expression until closing brace
                while (Index < Items.Length && Peek() != '}')
                {
                    LexPass(Peek());
                }

                if (Index < Items.Length && Peek() == '}')
                {
                    Pop(); // Pop '}'
                }

                currentLoc = new TextLocation(Line, Column);
                start = Index;
            }
            else if (c == '\\')
            {
                Pop(); // Pop backslash
                if (Index < Items.Length)
                {
                    Pop(); // Pop escaped character
                }
            }
            else if (c == '"')
            {
                // Emit final literal string segment before closing quote (if non-empty)
                if (Index > start)
                {
                    var span = new SourceSpan(FilePath, currentLoc, new TextLocation(Line, Column));
                    Tokens.Add(new Token(TokenType.StringLiteral, new string(Items[start..Index]), span));
                }

                var endLoc = new TextLocation(Line, Column);
                Pop(); // Pop closing '"'
                Tokens.Add(new Token(TokenType.InterpolationEnd, "\"", new SourceSpan(FilePath, endLoc, new TextLocation(Line, Column))));
                break;
            }
            else
            {
                Pop();
            }
        }
    }
    void PopStringOrChar(char quoteChar)
    {
        var initialLoc = new TextLocation(Line, Column);

        // Check if this is a triple-quoted multiline string (""")
        bool isMultiline = quoteChar == '"' && Peek(1) == '"' && Peek(2) == '"';
        int quoteLength = isMultiline ? 3 : 1;

        // Pop the opening quote(s)
        Pop(quoteLength);
        int start = Index;

        while (Index < Items.Length)
        {
            // 1. Check for string/char termination
            if (isMultiline)
            {
                if (IsMatch("\"\"\""))
                {
                    string multilineValue = new string(Items[start..Index]);
                    Pop(3); // Pop closing """
                    var span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
                    Tokens.Add(new Token(TokenType.StringLiteral, multilineValue, span));
                    return;
                }
            }
            else if (Peek() == quoteChar)
            {
                string singleLineValue = new string(Items[start..Index]);
                
                Pop(); // Pop closing quote
                
                var span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
                TokenType type = quoteChar == '\'' ? TokenType.CharLiteral : TokenType.StringLiteral;
                Tokens.Add(new Token(type, singleLineValue, span));
                return;
            }

            // 2. Handle escape sequences
            if (Peek() == '\\')
            {
                Pop(); // Pop '\\'
                if (Index < Items.Length)
                {
                    Pop(); // Pop escaped character safely
                }
            }
            else
            {
                Pop(); // Advance past regular characters (and newlines)
            }
        }

        // Fallback for unterminated literals at EOF
        var errSpan = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
        TokenType errType = quoteChar == '\'' ? TokenType.CharLiteral : TokenType.StringLiteral;
        Tokens.Add(new Token(errType, new string(Items[start..Index]), errSpan));
    }
    void PopIdentifierOrKeyword()
    {
        var initialLoc = new TextLocation(Line, Column);
        
        int start = Index;
        while (Index < Items.Length && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
        {
            Pop();
        }

        ReadOnlySpan<char> text = Items[start..Index];
        TokenType type = MatchKeyword(text);

        var span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
        Tokens.Add(new Token(type, new string(Items[start..Index]), span));
    }

    Token PopOperatorOrPunctuation()
    {
        var initialLoc = new TextLocation(Line, Column);

        char c = Peek();
        char next = Peek(1);
        char next2 = Peek(2);

        var span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));

        // 3-character operators
        TokenType tripleOp = (c, next, next2) switch
        {
            ('<', '<', '=') => TokenType.LeftShiftAssign,
            ('>', '>', '=') => TokenType.RightShiftAssign,
            _ => TokenType.Unknown
        };

        if (tripleOp != TokenType.Unknown)
        {
            Pop(3);
            return new Token(tripleOp, string.Join("", c, next, next2), span);
        }

        // 2-character operators
        TokenType doubleOp = (c, next) switch
        {
            ('=', '=') => TokenType.Equality,
            ('!', '=') => TokenType.Inequality,
            ('<', '=') => TokenType.LessThanEqual,
            ('>', '=') => TokenType.GreaterThanEqual,
            ('&', '&') => TokenType.LogicalAnd,
            ('|', '|') => TokenType.LogicalOr,
            ('+', '+') => TokenType.Increment,
            ('-', '-') => TokenType.Decrement,
            ('+', '=') => TokenType.AddAssign,
            ('-', '=') => TokenType.SubtractAssign,
            ('*', '=') => TokenType.MultiplyAssign,
            ('/', '=') => TokenType.DivideAssign,
            ('%', '=') => TokenType.ModuloAssign,
            ('&', '=') => TokenType.AndAssign,
            ('|', '=') => TokenType.OrAssign,
            ('^', '=') => TokenType.XorAssign,
            ('-', '>') => TokenType.ArrowSymbol,
            ('.', '.') => TokenType.RangeSymbol,
            ('<', '<') => TokenType.LeftShift,
            ('>', '>') => TokenType.RightShift,
            (':', ':') => TokenType.CastSymbol,
            ('=', '>') => TokenType.EqualArrow,
            _ => TokenType.Unknown
        };

        if (doubleOp != TokenType.Unknown)
        {
            Pop(2);
            span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
            return new Token(doubleOp, string.Join("", c, next), span);
        }

        // Single-character operators and punctuation
        TokenType singleOp = c switch
        {
            '+' => TokenType.Add,
            '-' => TokenType.Subtract,
            '*' => TokenType.Multiply,
            '/' => TokenType.Divide,
            '%' => TokenType.Modulo,
            '=' => TokenType.Assign,
            '<' => TokenType.LessThan,
            '>' => TokenType.GreaterThan,
            '!' => TokenType.LogicalNot,
            '&' => TokenType.BitwiseAnd,
            '|' => TokenType.BitwiseOr,
            '^' => TokenType.BitwiseXor,
            '~' => TokenType.Tilde,
            '?' => TokenType.Nullable,
            '@' => TokenType.At,
            '\\' => TokenType.Backslash,
            '(' => TokenType.ParenthesisOpen,
            ')' => TokenType.ParenthesisClose,
            '{' => TokenType.BracketOpen,
            '}' => TokenType.BracketClose,
            '[' => TokenType.SquareOpen,
            ']' => TokenType.SquareClose,
            ',' => TokenType.Comma,
            ';' => TokenType.Semicolon,
            '.' => TokenType.Dot,
            ':' => TokenType.Colon,
            '$' => TokenType.InterpolationStart,
            _ => TokenType.Unknown
        };

        if (singleOp != TokenType.Unknown)
        {
            Pop();
            span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
            return new Token(singleOp, c.ToString(), span);
        }

        return new Token(TokenType.Unknown, "", span);
    }

    // Zero-allocation fast keyword dispatch using Roslyn's Span switch engine
    static TokenType MatchKeyword(ReadOnlySpan<char> text) => text switch
    {
        // Literals
        "true" or "false" => TokenType.BooleanLiteral,
        "null" => TokenType.NullLiteral,
        "it" => TokenType.ItLiteral,
        "self" => TokenType.SelfLiteral,

        // Variable & Access Modifiers
        "val" or "var" or "const" => TokenType.VariableKind,
        "public" or "internal" or "protected" or "private" => TokenType.AccessModifierKind,
        "static" or "weak" or "partial" or "unsafe" => TokenType.MemberModifierKind,
        "virtual" or "abstract" or "sealed" or "impl" =>  TokenType.InheritanceModifierKind,
        "struct" or "record" or "class" or "fun" or "enum" or "trait" or "extension" or "extensions" or "annotation" or "constructor" or "destructor" => TokenType.InstanceKind,

        // Control Flow
        "if" => TokenType.IfKeyword,
        "else" => TokenType.ElseKeyword,
        "match" => TokenType.MatchKeyword,
        "case" => TokenType.CaseKeyword,
        "while" => TokenType.WhileKeyword,
        "for" => TokenType.ForKeyword,
        "in" => TokenType.InKeyword,
        "break" => TokenType.BreakKeyword,
        "continue" => TokenType.ContinueKeyword,

        // Modules
        "module" => TokenType.ModuleKeyword,
        "import" => TokenType.ImportKeyword,
        "from" => TokenType.FromKeyword,

        // Standalone Keywords
        "return" => TokenType.ReturnKeyword,
        "yield" => TokenType.YieldKeyword,
        "ref" => TokenType.RefKeyword,
        "concurrent" => TokenType.ConcurrentKeyword,
        "spawn" => TokenType.SpawnKeyword,
        "get" => TokenType.GetKeyword,
        "set" => TokenType.SetKeyword,
        "init" => TokenType.InitKeyword,

        // Special Operators
        "is" => TokenType.Is,
        "not" => TokenType.Not,
        "and" => TokenType.And,
        "or" => TokenType.Or,
        
        // Default Identifier
        _ => TokenType.Identifier
    };
}