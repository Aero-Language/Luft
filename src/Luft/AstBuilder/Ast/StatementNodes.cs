namespace Luft.AstBuilder.Ast;

internal record BlockStatementNode
(
    ValueList<StatementNode> Statements,
    SourceSpan Span
) : StatementNode(Span);

internal record VariableStatementNode
(
    ValueList<AnnotationNode> Annotations,
    DeclarationKind DeclKind,
    TypeRef Type,
    string Name,
    ExpressionNode? Initializer,
    SourceSpan Span
) : StatementNode(Span);

internal record ReturnStatementNode
(
    ExpressionNode? Value,
    SourceSpan Span
) : StatementNode(Span);

internal record BreakStatementNode
(
    SourceSpan Span
) : StatementNode(Span);

internal record ContinueStatementNode
(
    SourceSpan Span
) : StatementNode(Span);

internal record WhileStatementNode
(
    ExpressionNode? Condition,
    BlockStatementNode? Body,
    SourceSpan Span
) : StatementNode(Span);

internal record ExpressionStatementNode
(
    ExpressionNode Expression,
    SourceSpan Span
) : StatementNode(Span);

internal record ImportStatementNode
(
    string TargetPath,
    ValueList<string>? Imports, // Null here means importing everything from the module
    SourceSpan Span
) : StatementNode(Span);