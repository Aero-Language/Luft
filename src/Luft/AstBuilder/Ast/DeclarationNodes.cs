namespace Luft.AstBuilder.Ast;

internal record FunctionDeclarationNode
(
    ValueList<AnnotationExpressionNode> Annotations,
    AccessMod AccessMod,
    MemberMod MemberMods,
    TypeRef ReturnType,
    string Name,
    ValueList<TypeRef>? GenericParameters,
    ValueList<ParamNode> Parameters,
    BlockStatementNode? Body,
    SourceSpan Span
) : DeclarationNode(Span);

internal record StructDeclarationNode
(
    ValueList<AnnotationExpressionNode> Annotations,
    AccessMod AccessMod,
    MemberMod MemberMods,
    string Name,
    ValueList<DeclarationNode> Declarations,
    TypeRef? BaseStruct,
    ValueList<TypeRef> BaseInterfaces,
    SourceSpan Span
) : DeclarationNode(Span);

internal record ClassDeclarationNode
(
    ValueList<AnnotationExpressionNode> Annotations,
    AccessMod AccessMod,
    MemberMod MemberMods,
    string Name,
    ValueList<GenericParamNode> GenericParameters,
    ValueList<DeclarationNode> Declarations,
    TypeRef? BaseClass,
    ValueList<TypeRef> BaseInterfaces,
    SourceSpan Span
) : DeclarationNode(Span);

internal record InterfaceDeclarationNode
(
    ValueList<AnnotationExpressionNode> Annotations,
    AccessMod AccessMod,
    string Name,
    ValueList<TypeRef>? GenericParameters,
    ValueList<DeclarationNode> Declarations,
    ValueList<TypeRef> InheritedInterfaces,
    SourceSpan Span
) : DeclarationNode(Span);

internal record PropertyDeclarationNode
(
    ValueList<AnnotationExpressionNode> Annotations,
    AccessMod AccessMod,
    TypeRef Type,
    string Name,
    PropertyAccessorNode? Getter, // Null if write-only
    PropertyAccessorNode? Setter, // Null if read-only
    SourceSpan Span
) : DeclarationNode(Span);

internal record ConstructorDeclarationNode
(
    ValueList<AnnotationExpressionNode> Annotations,
    AccessMod AccessMod,
    ValueList<ParamNode> Parameters,
    BlockStatementNode Body,
    SourceSpan Span
) : DeclarationNode(Span);