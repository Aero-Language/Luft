using Luft.Lexer;

namespace Luft.AstBuilder.Ast;

internal record IfExpressionNode
(
    ExpressionNode Condition,
    BlockStatementNode ThenBody,
    BlockStatementNode? ElseBody,
    SourceSpan Span
) : ExpressionNode(null!, Span);

internal record ForExpressionNode
(
    ExpressionNode IndexDeclaration,
    ExpressionNode Condition,
    ExpressionNode Increment,
    BlockStatementNode Body,
    SourceSpan Span
) : ExpressionNode(null!, Span);

internal record ForInExpressionNode
(
    ExpressionNode Item,
    ExpressionNode Collection,
    BlockStatementNode Body,
    SourceSpan Span
) : ExpressionNode(null!, Span);

internal record MatchExpressionNode
(
    ExpressionNode Target,
    ValueList<CaseExpressionNode> Cases,
    SourceSpan Span
) : ExpressionNode(null!, Span);

internal record CaseExpressionNode
(
    ExpressionNode Condition,
    BlockStatementNode Body,
    SourceSpan Span
) : ExpressionNode(null!, Span);

internal record LiteralExpressionNode
(
    object? Value, // 'a', "something", 4, 3.14, 0xFF, 0b01 | The last two would resolve to an integer and be stored as one
    TokenType LiteralType,
    SourceSpan Span
) : ExpressionNode(null!, Span);

// I want an Identifier to be complete here and also be stored as a custom record with a ValueList<string> to store the individual locations and a quick method that will string.Join(Paths, ".")
internal record IdentifierExpressionNode
(
    string Name,
    SourceSpan Span
) : ExpressionNode(null!, Span);

// Could this be joined into Identifier? For a local var it would simply take the current path of where it was or just make the target nullable.
// This would make sense to me because there is not difference in doing "val firstValue: String = someValue or someObject.someMember".
// The only difference would be how they are resolved.
internal record MemberAccessExpressionNode
(
    ExpressionNode Target, // Identifier or MemberAccess
    SourceSpan Span
) : ExpressionNode(null!, Span);

internal record CallExpressionNode
(
    ExpressionNode Target,
    ValueList<ExpressionNode> Arguments,
    SourceSpan Span
) : ExpressionNode(null!, Span);

internal record IndexExpressionNode
(
    ExpressionNode Target,
    ExpressionNode Index,
    SourceSpan Span
) : ExpressionNode(null!, Span);

internal record BinaryExpressionNode
(
    ExpressionNode Left,
    string Operator, // We should switch this out with an enum or something
    ExpressionNode Right,
    SourceSpan Span
) : StatementNode(Span);

internal record UnaryExpressionNode
(
    string Operator,
    ExpressionNode Operand,
    SourceSpan Span
) : StatementNode(Span);

internal record AssignmentExpressionNode
(
    ExpressionNode Target,
    string Operator,
    ExpressionNode Value,
    SourceSpan Span
) : StatementNode(Span);

internal record SpawnExpressionNode
(
    BlockStatementNode Body, // The code executed by the Fiber
    SourceSpan Span
) : ExpressionNode(null!, Span);

// Represents closures: () => { ... } or () => singleExpression
internal record LambdaExpressionNode
(
    ValueList<ParamNode> Parameters,
    AstNode Body, // Can be ExpressionNode (=> x) or BlockStatementNode (=> { x })
    SourceSpan Span
) : ExpressionNode(null!, Span);

internal record StringInterpolationExpressionNode
(
    ValueList<ExpressionNode> Parts,
    SourceSpan Span
) : ExpressionNode(null!, Span);