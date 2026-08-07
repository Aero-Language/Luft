namespace Luft.AstBuilder.Ast;

internal record BlockStatementNode
(
    ValueList<StatementNode> Statements,
    SourceSpan Span
) : StatementNode(Span); 