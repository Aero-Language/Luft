namespace Luft.Parser.Lexer;

public enum TokenType
{
    // Whitespace & Comments (evaluated first)
    Whitespace,
    Comment,

    // Literals (FloatLiteral MUST have a lower value than IntLiteral)
    FloatLiteral,           // Tested BEFORE IntLiteral (e.g., matches "3.14" instead of stopping at "3")
    IntLiteral,
    StringLiteral,
    CharLiteral,
    BooleanLiteral,
    NullLiteral,
    SelfLiteral,

    // Identifiers
    Identifier, 
    
    // Keywords
    // Grouped keywords
    VariableKind,           // val, var, const
    AccessModifierKind,     // public, public, protected, private
    InstanceKind,           // struct, record, class, enum, trait, extension, annotation
    
    
    // Control-flow
    IfKeyword,
    ElseKeyword,
    MatchKeyword,
    CaseKeyword,
    WhileKeyword,
    ForKeyword,
    InKeyword,
    BreakKeyword,
    ContinueKeyword,
    
    // Modules
    ModuleKeyword,
    ImportKeyword,
    FromKeyword,
    
    // Standalone keywords
    FunctionKeyword,
    ReturnKeyword,
    RefKeyword,
    ConcurrentKeyword,
    SpawnKeyword,
    GetKeyword,
    SetKeyword,
    
    // Operators
    LeftShiftAssign,
    RightShiftAssign,
    Equality,
    Inequality,
    LessThanEqual,
    GreaterThanEqual,
    LogicalAnd,
    LogicalOr,
    Increment,
    Decrement,
    AddAssign,
    SubtractAssign,
    MultiplyAssign,
    DivideAssign,
    ModuloAssign,
    AndAssign,
    OrAssign,
    XORAssign,
    
    ArrowSymbol,
    SingleBlockSymbol,
    RangeSymbol,
    
    LeftShift,
    RightShift,
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Assign,
    LessThan,
    GreaterThan,
    Not,
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor,
    Tilde,
    Nullability,
    At,
    CastSymbol,
    
    // Punctuation
    EmptyStatement,
    ParenthesisOpen,
    ParenthesisClose,
    BracketOpen,
    BracketClose,
    SquareOpen,
    SquareClose,
    Comma,
    Semicolon,
    Dot,
    Colon,
    InterpolationStart,

    // Special markers & Fallbacks
    InterpolationEnd,       // End of an interpolated string
    Backslash,              // '\'
    Eof,
    Unknown                 // Fallback token type
}