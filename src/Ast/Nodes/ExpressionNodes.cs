using Luft.Lexer;
using Luft.Utility;

namespace Luft.Ast.Nodes;

public record BlockExpressionNode
(
    ValueList<StatementNode> Statements,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);

public record IfExpressionNode
(
    ExpressionNode Condition,
    BlockExpressionNode ThenBody,
    ValueList<(ExpressionNode condition, BlockExpressionNode body)> ElseIfs,
    BlockExpressionNode? ElseBody,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);

public record ForExpressionNode
(
    ParamNode Item,
    ExpressionNode Collection,
    BlockExpressionNode Body,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);

public record MatchExpressionNode
(
    ExpressionNode Target,
    ValueList<CaseExpressionNode> Cases,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);

public record CaseExpressionNode
(
    ExpressionNode Pattern,
    BlockExpressionNode Body,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);

public record LiteralExpressionNode
(
    object? Value, // 'a', "something", 4, 3.14, 0xFF, 0b01 | The last two would resolve to an integer and be stored as one
    TokenType LiteralType,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);

public record ArrayLiteralExpressionNode
(
    ValueList<ExpressionNode> Elements,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);

public record IdentifierExpressionNode
(
    string Name,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);

public record MemberAccessExpressionNode
(
    ExpressionNode Target,
    ExpressionNode Member, // Identifier or MemberAccess
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);

public record CallExpressionNode
(
    ExpressionNode Target,
    ValueList<ExpressionNode> Arguments,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);

public record IndexExpressionNode
(
    ExpressionNode Target,
    ExpressionNode Index,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);

public record BinaryExpressionNode
(
    ExpressionNode Left,
    Operator Operator,
    ExpressionNode Right,
    SourceSpan Span
) : ExpressionNode(AeroType.Void,Span);

public record RangeExpressionNode
(
    ExpressionNode? Left,
    ExpressionNode? Right,
    SourceSpan Span
) : ExpressionNode(AeroType.Void,Span);

public record UnaryExpressionNode
(
    Operator Operator,
    ExpressionNode Operand,
    bool IsPostFix,
    SourceSpan Span
) : ExpressionNode(AeroType.Void,Span);

// Represents closures: () => { ... } or () => singleExpression
public record LambdaExpressionNode
(
    ValueList<ParamNode> Parameters,
    BlockExpressionNode Body,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);

public record StringInterpolationExpressionNode
(
    ValueList<ExpressionNode> Parts,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);

public record ConcurrentExpressionNode
(
    BlockExpressionNode Body,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);
 
public record SpawnExpressionNode
(
    BlockExpressionNode Body,
    SourceSpan Span
) : ExpressionNode(AeroType.Void, Span);