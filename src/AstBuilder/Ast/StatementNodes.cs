using Luft.Utility;

namespace Luft.AstBuilder.Ast;

public record VariableStatementNode
(
    ValueList<AnnotationNode> Annotations,
    VariableKind DeclKind,
    TypeRef Type,
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

public record ImportStatementNode
(
    string TargetPath,
    ValueList<string> Imports, // Null here means importing everything from the module
    SourceSpan Span
) : StatementNode(Span);