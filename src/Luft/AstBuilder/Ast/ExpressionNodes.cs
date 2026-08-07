namespace Luft.AstBuilder.Ast;

internal record AnnotationExpressionNode
(
    string Name,
    ValueList<ParamNode> Parameters,
    SourceSpan Span
) : StatementNode(Span);