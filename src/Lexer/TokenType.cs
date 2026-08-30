namespace Luft.Lexer;

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
    ItLiteral,
    SelfLiteral,

    // Identifiers
    Identifier, 
    
    // Keywords
    // Grouped keywords
    VariableKind,           // val, var, const
    AccessModifierKind,     // public, internal, protected, private
    MemberModifierKind,     // static, weak, partial, unsafe
    InstanceKind,           // struct, record, class, enum, trait, extension(s), annotation
    
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
    YieldKeyword,
    RefKeyword,
    ConcurrentKeyword,
    SpawnKeyword,
    GetKeyword,
    SetKeyword,
    
    
    // Operators
    IsOperator,
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
    XorAssign,
    
    ArrowSymbol,
    EqualArrow,
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