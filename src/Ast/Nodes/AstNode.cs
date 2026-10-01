using Luft.Utility;

namespace Luft.Ast.Nodes;

public abstract record AstNode(SourceSpan Span);

public abstract record DeclarationNode(SourceSpan Span) : AstNode(Span);
public record ErrorDeclarationNode(SourceSpan Span) : DeclarationNode(Span);
public abstract record StatementNode(SourceSpan Span) : AstNode(Span);

public abstract record ExpressionNode(SourceSpan Span) : AstNode(Span);

// Placeholder the parser returns where an expression was expected but none could be parsed.
// The error is already reported at parse time, so later stages should stay silent about it.
public record ErrorExpressionNode(SourceSpan Span) : ExpressionNode(Span);

public record FileNode(ModuleDeclarationNode[] Modules, ImportStatementNode[] Imports, SourceSpan Span) : AstNode(Span);

public record PropertyAccessorNode
(
    AccessMod AccessMod,          // e.g., public get, private set
    BlockExpressionNode? Body,     // Null for auto-props / interface props
    PropertyAccessorKind Kind,
    SourceSpan Span
) : AstNode(Span);

public record AnnotationStatementNode
(
    string Name,
    ValueList<ExpressionNode> Parameters,
    SourceSpan Span
) : AstNode(Span);

public record ParamNode(
    string Name,
    AeroType Type,
    SourceSpan Span,
    ExpressionNode? Initializer = null,
    VariableKind VarKind = VariableKind.Val
) : AstNode(Span);