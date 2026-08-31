using Luft.Lexer;

namespace Luft.Utility;

public enum Operator
{
    Is,
    Not,
    And,
    Or,
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
    LogicalNot,
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor,
    Tilde,
    Nullability,
    At,
    CastSymbol,
}

public static class OperatorExtensions
{
    public static bool IsAssignment(this Operator op)
        => op switch
        {
            Operator.Assign
            or Operator.LeftShiftAssign
            or Operator.RightShiftAssign
            or Operator.AddAssign
            or Operator.SubtractAssign
            or Operator.MultiplyAssign
            or Operator.DivideAssign
            or Operator.ModuloAssign
            or Operator.AndAssign
            or Operator.OrAssign
            or Operator.XorAssign => true,
            _ => false
        };
    public static bool IsOperator(this TokenType type)
    {
        try
        {
            type.ToOperator();
            return true;
        }
        catch
        {
            return false;
        }
    }
    public static Operator ToOperator(this TokenType type)
        => type switch
        {
            TokenType.Is => Operator.Is,
            TokenType.Not => Operator.Not,
            TokenType.And => Operator.And,
            TokenType.Or => Operator.Or,
            TokenType.LeftShiftAssign => Operator.LeftShiftAssign,
            TokenType.RightShiftAssign => Operator.RightShiftAssign,
            TokenType.Equality => Operator.Equality,
            TokenType.Inequality => Operator.Inequality,
            TokenType.LessThanEqual => Operator.LessThanEqual,
            TokenType.GreaterThanEqual => Operator.GreaterThanEqual,
            TokenType.LogicalAnd => Operator.LogicalAnd,
            TokenType.LogicalOr => Operator.LogicalOr,
            TokenType.Increment => Operator.Increment,
            TokenType.Decrement => Operator.Decrement,
            TokenType.AddAssign => Operator.AddAssign,
            TokenType.SubtractAssign => Operator.SubtractAssign,
            TokenType.MultiplyAssign => Operator.MultiplyAssign,
            TokenType.DivideAssign => Operator.DivideAssign,
            TokenType.ModuloAssign => Operator.ModuloAssign,
            TokenType.AndAssign => Operator.AndAssign,
            TokenType.OrAssign => Operator.OrAssign,
            TokenType.XorAssign => Operator.XorAssign,
            TokenType.RangeSymbol => Operator.RangeSymbol,
            TokenType.LeftShift => Operator.LeftShift,
            TokenType.RightShift => Operator.RightShift,
            TokenType.Add => Operator.Add,
            TokenType.Subtract => Operator.Subtract,
            TokenType.Multiply => Operator.Multiply,
            TokenType.Divide => Operator.Divide,
            TokenType.Modulo => Operator.Modulo,
            TokenType.Assign => Operator.Assign,
            TokenType.LessThan => Operator.LessThan,
            TokenType.GreaterThan => Operator.GreaterThan,
            TokenType.LogicalNot => Operator.LogicalNot,
            TokenType.BitwiseAnd => Operator.BitwiseAnd,
            TokenType.BitwiseOr => Operator.BitwiseOr,
            TokenType.BitwiseXor => Operator.BitwiseXor,
            TokenType.Tilde => Operator.Tilde,
            TokenType.Nullable => Operator.Nullability,
            TokenType.At => Operator.At,
            TokenType.CastSymbol => Operator.CastSymbol,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
}