using Luft.Lexer;
using Luft.Utility;

namespace Luft.AstBuilder.Ast;

public record BlockExpressionNode
(
    ValueList<StatementNode> Statements,
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);

public record IfExpressionNode
(
    ExpressionNode Condition,
    BlockExpressionNode ThenBody,
    BlockExpressionNode? ElseBody,
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);

public record ForExpressionNode
(
    ExpressionNode IndexDeclaration,
    ExpressionNode Condition,
    ExpressionNode Increment,
    BlockExpressionNode Body,
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);

public record ForInExpressionNode
(
    ExpressionNode Item,
    ExpressionNode Collection,
    BlockExpressionNode Body,
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);

public record MatchExpressionNode
(
    ExpressionNode Target,
    ValueList<CaseExpressionNode> Cases,
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);

public record CaseExpressionNode
(
    ExpressionNode Condition,
    BlockExpressionNode Body,
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);

public record LiteralExpressionNode
(
    object? Value, // 'a', "something", 4, 3.14, 0xFF, 0b01 | The last two would resolve to an integer and be stored as one
    TokenType LiteralType,
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);

public record IdentifierExpressionNode
(
    string Name,
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);

// Could this be joined into Identifier? For a local var it would simply take the current path of where it was or just make the target nullable.
// This would make sense to me because there is not difference in doing "val firstValue: String = someValue or someObject.someMember".
// The only difference would be how they are resolved.
public record MemberAccessExpressionNode
(
    ExpressionNode Target, // Identifier or MemberAccess
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);

public record CallExpressionNode
(
    ExpressionNode Target,
    ValueList<ExpressionNode> Arguments,
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);

public record IndexExpressionNode
(
    ExpressionNode Target,
    ExpressionNode Index,
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);

public record BinaryExpressionNode
(
    ExpressionNode Left,
    string Operator, // We should switch this out with an enum or something
    ExpressionNode Right,
    SourceSpan Span
) : StatementNode(Span);

public record UnaryExpressionNode
(
    string Operator,
    ExpressionNode Operand,
    SourceSpan Span
) : StatementNode(Span);

public record AssignmentExpressionNode
(
    ExpressionNode Target,
    string Operator,
    ExpressionNode Value,
    SourceSpan Span
) : StatementNode(Span);

public record SpawnExpressionNode
(
    BlockExpressionNode Body, // The code executed by the Fiber
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);

// Represents closures: () => { ... } or () => singleExpression
public record LambdaExpressionNode
(
    ValueList<ParamNode> Parameters,
    AstNode Body, // Can be ExpressionNode (=> x) or BlockStatementNode (=> { x })
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);

public record StringInterpolationExpressionNode
(
    ValueList<ExpressionNode> Parts,
    SourceSpan Span
) : ExpressionNode(TypeRef.Void, Span);