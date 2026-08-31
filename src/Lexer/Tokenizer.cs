using System.Runtime.CompilerServices;
using Luft.Utility;

namespace Luft.Lexer;

public ref struct Tokenizer
{
    private string FilePath { get; set; }
    private int Line { get; set; } = 1;
    private int Column { get; set; } = 1;
        
    private ReadOnlySpan<char> source { get; set; }
    private int position { get; set; } = 0;
    private List<Token> tokens { get; set; }

    public Tokenizer() {}
    
    public List<Token> Tokenize(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException("File not found", filePath);

        FilePath = filePath;
            
        source = File.ReadAllText(filePath);
        tokens = new List<Token>(source.Length / 5);
        

        while (position < source.Length)
        {
            char c = Peek();

            LexPass(c);
        }

        var fileSpan = new SourceSpan(filePath, TextLocation.Zero, new TextLocation(source.Count('\n') + 1, source.Length - source.LastIndexOf('\n') - 1));
        tokens.Add(new Token(TokenType.Eof, "", fileSpan));
        return tokens;
    }
    
    void LexPass(char c)
    {
        // Whitespace
        if (char.IsWhiteSpace(c))
        {
            ConsumeWhitespace();
            return;
        }

        // Comments or Division Operators
        if (c == '/')
        {
            if (Peek(1) == '/' || Peek(1) == '*')
            {
                ConsumeComment();
                return;
            }
        }

        // Number Literals (Float vs Int)
        if (char.IsAsciiDigit(c) || (c == '.' && char.IsAsciiDigit(Peek(1))))
        {
            ConsumeNumber();
            return;
        }

        // Interpolated strings
        if (c is '$')
        {
            ConsumeInterpolatedString();
            return;
        }
        
        // String & Character Literals
        if (c is '"' or '\'')
        {
            ConsumeStringOrChar(c);
            return;
        }

        // Identifiers & Keywords
        if (char.IsLetter(c) || c == '_')
        {
            ConsumeIdentifierOrKeyword();
            return;
        }

        // Operators & Punctuation
        var opToken = ConsumeOperatorOrPunctuation();
        if (opToken.Type != TokenType.Unknown)
        {
            tokens.Add(opToken);
            return;
        }

        // Unknown / Fallback
        var span = new SourceSpan(FilePath, new TextLocation(Line, Column), new TextLocation(Line, Column + 1));
        tokens.Add(new Token(TokenType.Unknown, Peek().ToString(), span));
        Consume();
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    char Peek(int offset = 0)
    {
        int target = position + offset;
        return (uint)target < (uint)source.Length ? source[target] : '\0';
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    ReadOnlySpan<char> PeekRange(int start, int target) => (uint)target < (uint)source.Length ? source[start..target] : "\0";
    char Consume(int amount = 1)
    {
        var target = position + amount;
        var slice = PeekRange(position, target);
        var newLines = slice.Count('\n');
        Line += newLines;
        Column += amount;
        if (newLines > 0) Column = slice.Length - slice.LastIndexOf('\n');
        
        position = target;
        return Peek();
    }

    bool IsMatch(string text)
    {
        if (position + text.Length > source.Length)
            return false;

        return source.SequenceEqual(text);
    }
    void ConsumeWhitespace()
    {
        var initialLoc = new TextLocation(Line, Column);
        
        int start = position;
        char c = Peek();
        while (position < source.Length && char.IsWhiteSpace(c))
        {
            Consume();
            c = Peek();
        }
        var span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
        tokens.Add(new Token(TokenType.Whitespace, PeekRange(start, position).ToString(), span));
    }
    void ConsumeComment()
    {
        var initialLoc = new TextLocation(Line, Column);
        
        int start = position;
        if (Peek(1) == '/') // Line comment
        {
            Consume(2);
            while (position < source.Length && Peek() != '\n' && Peek() != '\r')
            {
                Consume();
            }
        }
        else if (Peek(1) == '*') // Block comment
        {
            Consume(2);
            while (position < source.Length)
            {
                if (Peek() == '*' && Peek(1) == '/')
                {
                    Consume(2);
                    break;
                }
                Consume();
            }
        }
        var span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
        tokens.Add(new Token(TokenType.Comment, PeekRange(start, position).ToString(), span));
    }
    void ConsumeNumber()
    {
        var initialLoc = new TextLocation(Line, Column);
        
        int start = position;
        bool isFloat = false;
        bool isHex = Peek() is '0' && Peek(1) is 'x';
        bool isBin = Peek() is '0' && Peek(1) is 'b';
        if (isHex || isBin) Consume(2);

        while (position < source.Length)
        {
            char current = Peek();
            
            if (char.IsAsciiDigit(current) && !isBin && !isHex) // handle int exclusively
            {
                Consume();
            }
            else if (current == '.' && !isFloat && char.IsAsciiDigit(Peek(1))) // handle float
            {
                isFloat = true;
                Consume();
            }
            else if (isHex && Extensions.HexChars.Contains(Peek())) // handle hexadecimal
            {
                Consume();
            }
            else if (isBin && Extensions.BinChars.Contains(Peek())) // handle binary
            {
                Consume();
            }
            else
            {
                break;
            }
        }

        TokenType type = isFloat ? TokenType.FloatLiteral : TokenType.IntLiteral;
        var span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
        tokens.Add(new Token(type, PeekRange(start, position).ToString(), span));
    }
    void ConsumeInterpolatedString()
    {
        var startLoc = new TextLocation(Line, Column);

        // Emit InterpolationStart for $"
        tokens.Add(new Token(TokenType.InterpolationStart, "$\"", new SourceSpan(FilePath, startLoc, new TextLocation(Line, Column + 2))));
        Consume(2); // Consume $"

        int start = position;
        var currentLoc = new TextLocation(Line, Column);

        while (position < source.Length)
        {
            char c = Peek();

            if (c == '{')
            {
                // Emit literal string preceding the interpolated expression (if any)
                if (position > start)
                {
                    var span = new SourceSpan(FilePath, currentLoc, new TextLocation(Line, Column));
                    tokens.Add(new Token(TokenType.StringLiteral, source[start..position].ToString(), span));
                }

                Consume(); // Consume '{'
                
                // Lex tokens inside expression until closing brace
                while (position < source.Length && Peek() != '}')
                {
                    LexPass(Peek());
                }

                if (position < source.Length && Peek() == '}')
                {
                    Consume(); // Consume '}'
                }

                currentLoc = new TextLocation(Line, Column);
                start = position;
            }
            else if (c == '\\')
            {
                Consume(); // Consume backslash
                if (position < source.Length)
                {
                    Consume(); // Consume escaped character
                }
            }
            else if (c == '"')
            {
                // Emit final literal string segment before closing quote (if non-empty)
                if (position > start)
                {
                    var span = new SourceSpan(FilePath, currentLoc, new TextLocation(Line, Column));
                    tokens.Add(new Token(TokenType.StringLiteral, source[start..position].ToString(), span));
                }

                var endLoc = new TextLocation(Line, Column);
                Consume(); // Consume closing '"'
                tokens.Add(new Token(TokenType.InterpolationEnd, "\"", new SourceSpan(FilePath, endLoc, new TextLocation(Line, Column))));
                break;
            }
            else
            {
                Consume();
            }
        }
    }
    void ConsumeStringOrChar(char quoteChar)
    {
        var initialLoc = new TextLocation(Line, Column);

        // Check if this is a triple-quoted multiline string (""")
        bool isMultiline = quoteChar == '"' && Peek(1) == '"' && Peek(2) == '"';
        int quoteLength = isMultiline ? 3 : 1;

        // Consume the opening quote(s)
        Consume(quoteLength);
        int start = position;

        while (position < source.Length)
        {
            // 1. Check for string/char termination
            if (isMultiline)
            {
                if (IsMatch("\"\"\""))
                {
                    string multilineValue = source[start..position].ToString();
                    Consume(3); // Consume closing """
                    var span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
                    tokens.Add(new Token(TokenType.StringLiteral, multilineValue, span));
                    return;
                }
            }
            else if (Peek() == quoteChar)
            {
                string singleLineValue = source[start..position].ToString();
                
                Consume(); // Consume closing quote
                
                var span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
                TokenType type = quoteChar == '\'' ? TokenType.CharLiteral : TokenType.StringLiteral;
                tokens.Add(new Token(type, singleLineValue, span));
                return;
            }

            // 2. Handle escape sequences
            if (Peek() == '\\')
            {
                Consume(); // Consume '\\'
                if (position < source.Length)
                {
                    Consume(); // Consume escaped character safely
                }
            }
            else
            {
                Consume(); // Advance past regular characters (and newlines)
            }
        }

        // Fallback for unterminated literals at EOF
        var errSpan = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
        TokenType errType = quoteChar == '\'' ? TokenType.CharLiteral : TokenType.StringLiteral;
        tokens.Add(new Token(errType, source[start..position].ToString(), errSpan));
    }
    void ConsumeIdentifierOrKeyword()
    {
        var initialLoc = new TextLocation(Line, Column);
        
        int start = position;
        while (position < source.Length && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
        {
            Consume();
        }

        ReadOnlySpan<char> text = source.Slice(start, position - start);
        TokenType type = MatchKeyword(text);

        var span = new SourceSpan(FilePath, initialLoc, new TextLocation(Line, Column));
        tokens.Add(new Token(type, source[start..position].ToString(), span));
    }
    Token ConsumeOperatorOrPunctuation()
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
            Consume(3);
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
            Consume(2);
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
            Consume();
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

        // Special Operators
        "is" => TokenType.Is,
        "not" => TokenType.Not,
        "and" => TokenType.And,
        "or" => TokenType.Or,
        
        // Default Identifier
        _ => TokenType.Identifier
    };
}