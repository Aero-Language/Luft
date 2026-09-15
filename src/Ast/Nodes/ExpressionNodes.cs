using Luft.Lexer;
using Luft.Utility;

namespace Luft.Ast.Nodes;

public record BlockExpressionNode
(
    bool IsSingleLine,
    ValueList<StatementNode> Statements,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record IfExpressionNode
(
    ExpressionNode Condition,
    BlockExpressionNode ThenBody,
    ValueList<(ExpressionNode condition, BlockExpressionNode body)> ElseIfs,
    BlockExpressionNode? ElseBody,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record ForExpressionNode
(
    ParamNode Item,
    ExpressionNode Collection,
    BlockExpressionNode Body,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record MatchExpressionNode
(
    ExpressionNode Target,
    ValueList<CaseExpressionNode> Cases,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record CaseExpressionNode
(
    ExpressionNode Pattern,
    BlockExpressionNode Body,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record LiteralExpressionNode
(
    object Value, // 'a', "something", 4, 3.14, 0xFF, 0b01 | The last two would resolve to an integer and be stored as one
    TokenType LiteralType,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record ArrayLiteralExpressionNode
(
    ValueList<ExpressionNode> Elements,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record IdentifierExpressionNode
(
    string Name,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record MemberAccessExpressionNode
(
    ExpressionNode Target,
    ExpressionNode Member, // Identifier or MemberAccess
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record CallExpressionNode
(
    ExpressionNode Target,
    ValueList<ExpressionNode> Arguments,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record IndexExpressionNode
(
    ExpressionNode Target,
    ExpressionNode Index,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record BinaryExpressionNode
(
    ExpressionNode Left,
    Operator Operator,
    ExpressionNode Right,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record RangeExpressionNode
(
    ExpressionNode? Left,
    ExpressionNode? Right,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record UnaryExpressionNode
(
    Operator Operator,
    ExpressionNode Operand,
    bool IsPostFix,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

// Represents closures: () => { ... } or () => singleExpression
public record LambdaExpressionNode
(
    ValueList<ParamNode> Parameters,
    BlockExpressionNode Body,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record StringInterpolationExpressionNode
(
    ValueList<ExpressionNode> Parts,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);

public record ConcurrentExpressionNode
(
    BlockExpressionNode Body,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);
 
public record SpawnExpressionNode
(
    BlockExpressionNode Body,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);
public record ScopedExpressionNode
(
    ExpressionNode Scoped,
    SourceSpan Span
) : ExpressionNode(Span, AeroType.Void);