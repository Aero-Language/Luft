namespace Aero_Compiler.Lexer;

internal enum TokenType
{
    // Priority 10–20: Whitespace & Comments (evaluated first)
    Whitespace   = 10,
    Comment      = 20,

    // Priority 30–60: Literals (FloatLiteral MUST have a lower value than IntLiteral)
    FloatLiteral = 30, // Tested BEFORE IntLiteral (e.g., matches "3.14" instead of stopping at "3")
    IntLiteral   = 40,
    StringLiteral= 50,
    CharLiteral  = 60,

    // Priority 70–90: Identifiers, Operators, & Punctuation
    Identifier   = 70, // Promoted to Keyword if present in the Keywords lookup table
    Operator     = 80,
    Punctuation  = 90,

    // Priority 100+: Special markers & Fallbacks
    Eof          = 100,
    Keyword      = 900, // Not matched directly by Regex (handled via Keyword table lookup)
    Unknown      = 999  // Fallback token type
}