using Luft.Utility;

namespace Luft.Ast.Nodes;

public record VariableStatementNode
(
    VariableKind VarKind,
    AeroType Type,
    string Name,
    ExpressionNode? Initializer,
    SourceSpan Span
) : StatementNode(Span);

public record ReturnStatementNode
(
    ExpressionNode? Value,
    SourceSpan Span
) : StatementNode(Span);

public record BreakStatementNode
(
    SourceSpan Span
) : StatementNode(Span);

public record ContinueStatementNode
(
    SourceSpan Span
) : StatementNode(Span);

public record WhileStatementNode
(
    ExpressionNode? Condition,
    BlockExpressionNode? Body,
    SourceSpan Span
) : StatementNode(Span);

public record ExpressionStatementNode
(
    ExpressionNode Expression,
    SourceSpan Span
) : StatementNode(Span);

public record AssignmentStatementNode
(
    ExpressionNode Target,
    Operator Operator,
    ExpressionNode Value,
    SourceSpan Span
) : StatementNode(Span);

public record EmptyStatementNode(SourceSpan Span) : StatementNode(Span);

public record ImportStatementNode
(
    string TargetPath,
    ValueList<string> Imports, // Null here means importing everything from the module
    SourceSpan Span
) : StatementNode(Span);