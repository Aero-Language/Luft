using Luft.Utility;

namespace Luft.AstBuilder.Ast;

public abstract record AstNode(SourceSpan Span);

public abstract record DeclarationNode(SourceSpan Span) : AstNode(Span);
public record ErrorDeclarationNode(SourceSpan Span) : DeclarationNode(Span);
public abstract record StatementNode(SourceSpan Span) : AstNode(Span);
public abstract record ExpressionNode(TypeRef NodeType, SourceSpan Span) : AstNode(Span);

public record FileNode(ModuleDeclarationNode[] Modules, ImportStatementNode[] Imports, SourceSpan Span) : AstNode(Span);
public record GenericParamNode(string Name, TypeRef TypeConstraint, SourceSpan Span) : AstNode(Span);

public record PropertyAccessorNode
(
    AccessMod AccessMod,          // e.g., public get, private set
    BlockExpressionNode? Body,     // Null for auto-props / interface props
    SourceSpan Span
) : AstNode(Span);

public record AnnotationNode
(
    string Name,
    ValueList<ExpressionNode> Parameters,
    SourceSpan Span
) : AstNode(Span);