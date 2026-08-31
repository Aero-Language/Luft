using Luft.Utility;

namespace Luft.Ast.Nodes;

public abstract record AstNode(SourceSpan Span);

public abstract record DeclarationNode(SourceSpan Span) : AstNode(Span);
public record ErrorDeclarationNode(SourceSpan Span) : DeclarationNode(Span);
public abstract record StatementNode(SourceSpan Span) : AstNode(Span);
public abstract record ExpressionNode(AeroType NodeType, SourceSpan Span) : AstNode(Span);

public record FileNode(ModuleDeclarationNode[] Modules, ImportStatementNode[] Imports, DeclarationNode[] Globals, SourceSpan Span) : AstNode(Span);
public record GenericParamNode(string Name, AeroType TypeConstraint, SourceSpan Span) : AstNode(Span);

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