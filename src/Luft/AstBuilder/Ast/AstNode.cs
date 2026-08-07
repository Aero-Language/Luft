namespace Luft.AstBuilder.Ast;

internal abstract record AstNode(SourceSpan Span);

internal abstract record DeclarationNode(SourceSpan Span) : AstNode(Span);
internal abstract record StatementNode(SourceSpan Span) : AstNode(Span);
internal abstract record ExpressionNode(TypeRef NodeType, SourceSpan Span) : AstNode(Span);

internal record ProgramNode(DeclarationNode[] Declarations, SourceSpan Span) : AstNode(Span);
internal record GenericParamNode(string Name, ValueList<TypeRef> Constraints, SourceSpan Span) : AstNode(Span);
internal record PropertyAccessorNode
(
    AccessMod AccessMod,          // e.g., public get, private set
    BlockStatementNode? Body,     // Null for auto-props / interface props
    SourceSpan Span
) : AstNode(Span);