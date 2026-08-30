using System.Runtime.CompilerServices;
using Luft.Utility;

namespace Luft.Lexer;

public static class Lexer
{
    public static List<Token> Tokenize(string filePath)
    {
        LexerHelper lex = new LexerHelper(filePath);

        while (lex.position < lex.source.Length)
        {
            char c = lex.Peek();

            lex.LexPass(c);
        }

        var fileSpan = new SourceSpan(filePath, TextLocation.Zero, new TextLocation(lex.source.Count('\n') + 1, lex.source.Length - lex.source.LastIndexOf('\n') - 1));
        lex.tokens.Add(new Token(TokenType.Eof, "", fileSpan));
        return lex.tokens;
    }

    private ref struct LexerHelper
    {
        private int _line = 1;
        private int _column = 1;
        private readonly string _filePath;
        
        public readonly ReadOnlySpan<char> source;
        public int position = 0;
        public readonly List<Token> tokens;

        public LexerHelper(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("File not found", filePath);

            _filePath = filePath;
            
            source = File.ReadAllText(filePath);
            tokens = new List<Token>(source.Length / 5);
        }
        
        public void LexPass(char c)
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
            var span = new SourceSpan(_filePath, new TextLocation(_line, _column), new TextLocation(_line, _column + 1));
            tokens.Add(new Token(TokenType.Unknown, Peek().ToString(), span));
            Skip();
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public char Peek(int offset = 0)
        {
            int target = position + offset;
            return (uint)target < (uint)source.Length ? source[target] : '\0';
        }
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ReadOnlySpan<char> PeekRange(int start, int target)
        {
            return (uint)target < (uint)source.Length ? source[start..target] : "\0";
        }
        
        private char Skip(int offset = 1)
        {
            var target = position + offset;
            var slice = PeekRange(position, target);
            var newLines = slice.Count('\n');
            _line += newLines;
            _column += offset;
            if (newLines > 0) _column = slice.Length - slice.LastIndexOf('\n');
            
            position = target;
            return Peek();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsMatch(string text)
        {
            if (position + text.Length > source.Length)
                return false;

            return source.SequenceEqual(text);
        }

        private void ConsumeWhitespace()
        {
            var initialLoc = new TextLocation(_line, _column);
            
            int start = position;
            char c = Peek();
            while (position < source.Length && char.IsWhiteSpace(c))
            {
                Skip();
                c = Peek();
            }
            var span = new SourceSpan(_filePath, initialLoc, new TextLocation(_line, _column));
            tokens.Add(new Token(TokenType.Whitespace, PeekRange(start, position).ToString(), span));
        }

        private void ConsumeComment()
        {
            var initialLoc = new TextLocation(_line, _column);
            
            int start = position;
            if (Peek(1) == '/') // Line comment
            {
                Skip(2);
                while (position < source.Length && Peek() != '\n' && Peek() != '\r')
                {
                    Skip();
                }
            }
            else if (Peek(1) == '*') // Block comment
            {
                Skip(2);
                while (position < source.Length)
                {
                    if (Peek() == '*' && Peek(1) == '/')
                    {
                        Skip(2);
                        break;
                    }
                    Skip();
                }
            }
            var span = new SourceSpan(_filePath, initialLoc, new TextLocation(_line, _column));
            tokens.Add(new Token(TokenType.Comment, PeekRange(start, position).ToString(), span));
        }

        private void ConsumeNumber()
        {
            var initialLoc = new TextLocation(_line, _column);
            
            int start = position;
            bool isFloat = false;

            while (position < source.Length)
            {
                char current = Peek();

                if (char.IsAsciiDigit(current)) // handle int
                {
                    Skip();
                }
                else if (current == '.' && !isFloat && char.IsAsciiDigit(Peek(1))) // handle float
                {
                    isFloat = true;
                    Skip();
                }
                else if (current == 'x' && Extensions.HexChars.Contains(Peek(1)) || Peek(1) == '_') // handle hexadecimal
                {
                    Skip();
                }
                else if (current == 'x' && Extensions.BinChars.Contains(Peek(1)) || Peek(1) == '_') // handle binary
                {
                    Skip();
                }
                else
                {
                    break;
                }
            }

            TokenType type = isFloat ? TokenType.FloatLiteral : TokenType.IntLiteral;
            var span = new SourceSpan(_filePath, initialLoc, new TextLocation(_line, _column));
            tokens.Add(new Token(type, PeekRange(start, position).ToString(), span));
        }

        private void ConsumeInterpolatedString()
        {
            var startLoc = new TextLocation(_line, _column);

            // Emit InterpolationStart for $"
            tokens.Add(new Token(TokenType.InterpolationStart, "$\"", new SourceSpan(_filePath, startLoc, new TextLocation(_line, _column + 2))));
            Skip(2); // Skip $"

            int start = position;
            var currentLoc = new TextLocation(_line, _column);

            while (position < source.Length)
            {
                char c = Peek();

                if (c == '{')
                {
                    // Emit literal string preceding the interpolated expression (if any)
                    if (position > start)
                    {
                        var span = new SourceSpan(_filePath, currentLoc, new TextLocation(_line, _column));
                        tokens.Add(new Token(TokenType.StringLiteral, source[start..position].ToString(), span));
                    }

                    Skip(); // Consume '{'
                    
                    // Lex tokens inside expression until closing brace
                    while (position < source.Length && Peek() != '}')
                    {
                        LexPass(Peek());
                    }

                    if (position < source.Length && Peek() == '}')
                    {
                        Skip(); // Consume '}'
                    }

                    currentLoc = new TextLocation(_line, _column);
                    start = position;
                }
                else if (c == '\\')
                {
                    Skip(); // Skip backslash
                    if (position < source.Length)
                    {
                        Skip(); // Skip escaped character
                    }
                }
                else if (c == '"')
                {
                    // Emit final literal string segment before closing quote (if non-empty)
                    if (position > start)
                    {
                        var span = new SourceSpan(_filePath, currentLoc, new TextLocation(_line, _column));
                        tokens.Add(new Token(TokenType.StringLiteral, source[start..position].ToString(), span));
                    }

                    var endLoc = new TextLocation(_line, _column);
                    Skip(); // Consume closing '"'
                    tokens.Add(new Token(TokenType.InterpolationEnd, "\"", new SourceSpan(_filePath, endLoc, new TextLocation(_line, _column))));
                    break;
                }
                else
                {
                    Skip();
                }
            }
        }
        
        private void ConsumeStringOrChar(char quoteChar)
        {
            var initialLoc = new TextLocation(_line, _column);

            // Check if this is a triple-quoted multiline string (""")
            bool isMultiline = quoteChar == '"' && Peek(1) == '"' && Peek(2) == '"';
            int quoteLength = isMultiline ? 3 : 1;

            // Skip the opening quote(s)
            Skip(quoteLength);
            int start = position;

            while (position < source.Length)
            {
                // 1. Check for string/char termination
                if (isMultiline)
                {
                    if (IsMatch("\"\"\""))
                    {
                        string multilineValue = source[start..position].ToString();
                        Skip(3); // Consume closing """
                        var span = new SourceSpan(_filePath, initialLoc, new TextLocation(_line, _column));
                        tokens.Add(new Token(TokenType.StringLiteral, multilineValue, span));
                        return;
                    }
                }
                else if (Peek() == quoteChar)
                {
                    string singleLineValue = source[start..position].ToString();
                    
                    Skip(); // Consume closing quote
                    
                    var span = new SourceSpan(_filePath, initialLoc, new TextLocation(_line, _column));
                    TokenType type = quoteChar == '\'' ? TokenType.CharLiteral : TokenType.StringLiteral;
                    tokens.Add(new Token(type, singleLineValue, span));
                    return;
                }

                // 2. Handle escape sequences
                if (Peek() == '\\')
                {
                    Skip(); // Skip '\\'
                    if (position < source.Length)
                    {
                        Skip(); // Skip escaped character safely
                    }
                }
                else
                {
                    Skip(); // Advance past regular characters (and newlines)
                }
            }

            // Fallback for unterminated literals at EOF
            var errSpan = new SourceSpan(_filePath, initialLoc, new TextLocation(_line, _column));
            TokenType errType = quoteChar == '\'' ? TokenType.CharLiteral : TokenType.StringLiteral;
            tokens.Add(new Token(errType, source[start..position].ToString(), errSpan));
        }

        private void ConsumeIdentifierOrKeyword()
        {
            var initialLoc = new TextLocation(_line, _column);
            
            int start = position;
            while (position < source.Length && (char.IsLetterOrDigit(Peek()) || Peek() == '_'))
            {
                Skip();
            }

            ReadOnlySpan<char> text = source.Slice(start, position - start);
            TokenType type = MatchKeyword(text);

            var span = new SourceSpan(_filePath, initialLoc, new TextLocation(_line, _column));
            tokens.Add(new Token(type, source[start..position].ToString(), span));
        }
        
        private Token ConsumeOperatorOrPunctuation()
        {
            var initialLoc = new TextLocation(_line, _column);
            
            char c = Peek();
            char next = Peek(1);
            char next2 = Peek(2);
            
            var span = new SourceSpan(_filePath, initialLoc, new TextLocation(_line, _column));

            // 3-character operators
            TokenType tripleOp = (c, next, next2) switch
            {
                ('<', '<', '=') => TokenType.LeftShiftAssign,
                ('>', '>', '=') => TokenType.RightShiftAssign,
                _ => TokenType.Unknown
            };
            
            if (tripleOp != TokenType.Unknown)
            {
                Skip(3);
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
                (';', ';') => TokenType.EmptyStatement,
                ('=', '>') => TokenType.EqualArrow,
                _ => TokenType.Unknown
            };
            
            if (doubleOp != TokenType.Unknown)
            {
                Skip(2);
                span = new SourceSpan(_filePath, initialLoc, new TextLocation(_line, _column));
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
                '!' => TokenType.Not,
                '&' => TokenType.BitwiseAnd,
                '|' => TokenType.BitwiseOr,
                '^' => TokenType.BitwiseXor,
                '~' => TokenType.Tilde,
                '?' => TokenType.Nullability,
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
                Skip();
                span = new SourceSpan(_filePath, initialLoc, new TextLocation(_line, _column));
                return new Token(singleOp, c.ToString(), span);
            }
            
            return new Token(TokenType.Unknown, "", span);
        }
        
        
        // Zero-allocation fast keyword dispatch using Roslyn's Span switch engine
        private static TokenType MatchKeyword(ReadOnlySpan<char> text) => text switch
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
            "struct" or "record" or "class" or "enum" or "trait" or "extension" or "extensions" or "annotation" => TokenType.InstanceKind,

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
            "fun" => TokenType.FunctionKeyword,
            "return" => TokenType.ReturnKeyword,
            "yield" => TokenType.YieldKeyword,
            "ref" => TokenType.RefKeyword,
            "concurrent" => TokenType.ConcurrentKeyword,
            "spawn" => TokenType.SpawnKeyword,
            "get" => TokenType.GetKeyword,
            "set" => TokenType.SetKeyword,

            // Special Opertors
            "is" => TokenType.IsOperator,
            
            // Default Identifier
            _ => TokenType.Identifier
        };
    }
}