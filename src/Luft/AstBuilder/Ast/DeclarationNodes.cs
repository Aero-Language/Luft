namespace Luft.AstBuilder.Ast;

internal record FunctionDeclarationNode
(
    ValueList<AnnotationStatementNode> Annotations,
    AccessMod AccessMod,
    MemberMod MemberMods,
    TypeRef ReturnType,
    string Name,
    ValueList<TypeRef>? GenericParameters,
    ValueList<ParamNode> Parameters,
    BlockStatementNode? Body,
    SourceSpan Span
) : DeclarationNode(Span);

internal record ExtensionDeclarationNode
(
    FunctionDeclarationNode? Function,
    PropertyDeclarationNode? Property,
    TypeRef TargetType,
    SourceSpan Span
) : DeclarationNode(Span);

internal record ExtensionBlockDeclarationNode
(
    TypeRef TargetType,
    ValueList<ExtensionDeclarationNode> Extensions,
    SourceSpan Span
) : DeclarationNode(Span);

internal record StructDeclarationNode
(
    ValueList<AnnotationStatementNode> Annotations,
    AccessMod AccessMod,
    MemberMod MemberMods,
    string Name,
    ValueList<DeclarationNode> Declarations,
    TypeRef? BaseStruct,
    ValueList<TypeRef> Traits,
    SourceSpan Span
) : DeclarationNode(Span);

internal record AnnotationDeclarationNode
(
    ValueList<AnnotationStatementNode> Annotations,
    AccessMod AccessMod,
    MemberMod MemberMods,
    string Name,
    ValueList<DeclarationNode> Declarations,
    TypeRef? BaseStruct,
    ValueList<TypeRef> Traits,
    SourceSpan Span
) : DeclarationNode(Span);

internal record RecordDeclarationNode
(
    ValueList<AnnotationStatementNode> Annotations,
    AccessMod AccessMod,
    MemberMod MemberMods,
    string Name,
    ValueList<VariableStatementNode> Properties,
    TypeRef? BaseRecord,
    SourceSpan Span
) : DeclarationNode(Span);

internal record ClassDeclarationNode
(
    ValueList<AnnotationStatementNode> Annotations,
    AccessMod AccessMod,
    MemberMod MemberMods,
    string Name,
    ValueList<GenericParamNode> GenericParameters,
    ValueList<DeclarationNode> Declarations,
    TypeRef? BaseClass,
    ValueList<TypeRef> Traits,
    SourceSpan Span
) : DeclarationNode(Span);

internal record TraitDeclarationNode
(
    ValueList<AnnotationStatementNode> Annotations,
    AccessMod AccessMod,
    string Name,
    ValueList<TypeRef>? GenericParameters,
    ValueList<DeclarationNode> Declarations,
    ValueList<TypeRef> Traits,
    SourceSpan Span
) : DeclarationNode(Span);

internal record EnumMemberNode
(
    ValueList<AnnotationStatementNode> Annotations,
    string Name,
    ExpressionNode? Value, // Handles the '= "steve"' or '= 1' part
    SourceSpan Span
) : DeclarationNode(Span); // Inheriting from DeclarationNode makes symbol table registration easier

// Represents the enum or enum class declaration itself
internal record EnumDeclarationNode
(
    ValueList<AnnotationStatementNode> Annotations,
    AccessMod AccessMod,
    bool IsEnumClass, // true for 'enum class', false for standard 'enum'
    string Name,
    TypeRef? BaseType, // Captures the ': String' part
    ValueList<EnumMemberNode> Members,
    SourceSpan Span
) : DeclarationNode(Span);

internal record PropertyDeclarationNode
(
    ValueList<AnnotationStatementNode> Annotations,
    AccessMod AccessMod,
    DeclarationKind DeclKind,
    TypeRef Type,
    string Name,
    PropertyAccessorNode? Getter, // Null if write-only
    PropertyAccessorNode? Setter, // Null if read-only
    SourceSpan Span
) : DeclarationNode(Span);

internal record ConstructorDeclarationNode
(
    ValueList<AnnotationStatementNode> Annotations,
    AccessMod AccessMod,
    ValueList<ParamNode> Parameters,
    BlockStatementNode Body,
    SourceSpan Span
) : DeclarationNode(Span);

internal record DestructorDeclarationNode
(
    ValueList<AnnotationStatementNode> Annotations,
    BlockStatementNode Body,
    SourceSpan Span
) : DeclarationNode(Span);

internal record ModuleDeclarationNode
(
    string ModulePath,
    ValueList<DeclarationNode> Declarations, 
    SourceSpan Span
) : DeclarationNode(Span);