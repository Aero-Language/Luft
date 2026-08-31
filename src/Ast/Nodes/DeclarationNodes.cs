using Luft.Utility;

namespace Luft.Ast.Nodes;

public record FunctionDeclarationNode
(
    ValueList<AnnotationNode> Annotations,
    AccessMod AccessMod,
    InheritanceMod InheritanceMod,
    MemberMod MemberMods,
    AeroType ReturnType,
    string Name,
    ValueList<GenericParamNode>? GenericParameters,
    ValueList<ParamNode> Parameters,
    BlockExpressionNode? Body,
    SourceSpan Span
) : DeclarationNode(Span);

public record ExtensionDeclarationNode
(
    DeclarationNode Extension,
    AeroType TargetType,
    SourceSpan Span
) : DeclarationNode(Span);

public record ExtensionBlockDeclarationNode
(
    AccessMod AccessMod,
    AeroType TargetType,
    ValueList<ExtensionDeclarationNode> Extensions,
    SourceSpan Span
) : DeclarationNode(Span);

public record StructDeclarationNode
(
    ValueList<AnnotationNode> Annotations,
    AccessMod AccessMod,
    InheritanceMod InheritanceMod,
    MemberMod MemberMods,
    string Name,
    ValueList<DeclarationNode> Declarations,
    ValueList<AeroType> Implements,
    SourceSpan Span
) : DeclarationNode(Span);

public record AnnotationDeclarationNode
(
    ValueList<AnnotationNode> Annotations,
    AccessMod AccessMod,
    MemberMod MemberMods,
    string Name,
    ValueList<GenericParamNode> GenericParameters,
    ValueList<DeclarationNode> Declarations,
    SourceSpan Span
) : DeclarationNode(Span);

public record RecordDeclarationNode
(
    ValueList<AnnotationNode> Annotations,
    AccessMod AccessMod,
    InheritanceMod InheritanceMod,
    MemberMod MemberMods,
    string Name,
    ValueList<GenericParamNode> GenericParameters,
    ValueList<DeclarationNode> Properties,
    ValueList<AeroType> Implements,
    SourceSpan Span
) : DeclarationNode(Span);

public record ClassDeclarationNode
(
    ValueList<AnnotationNode> Annotations,
    AccessMod AccessMod,
    InheritanceMod InheritanceMod,
    MemberMod MemberMods,
    string Name,
    ValueList<GenericParamNode> GenericParameters,
    ValueList<DeclarationNode> Declarations,
    ValueList<AeroType> Implements,
    SourceSpan Span
) : DeclarationNode(Span);

public record TraitDeclarationNode
(
    ValueList<AnnotationNode> Annotations,
    AccessMod AccessMod,
    InheritanceMod InheritanceMod,
    string Name,
    ValueList<GenericParamNode> GenericParameters,
    ValueList<DeclarationNode> Declarations,
    ValueList<AeroType> Traits,
    SourceSpan Span
) : DeclarationNode(Span);

public record EnumMemberNode
(
    string Name,
    ExpressionNode? Value, // Handles the '= "steve"' or '= 1' part
    SourceSpan Span
) : DeclarationNode(Span); // Inheriting from DeclarationNode makes symbol table registration easier

// Represents the enum or enum class declaration itself
public record EnumDeclarationNode
(
    ValueList<AnnotationNode> Annotations,
    AccessMod AccessMod,
    string Name,
    AeroType? MemberType, // This should be constrained to integer-only
    ValueList<ParamNode>? Parameters, // Null here indicates a normal enum instead of an enum class
    ValueList<EnumMemberNode> Members,
    SourceSpan Span
) : DeclarationNode(Span);

public record PropertyDeclarationNode
(
    ValueList<AnnotationNode> Annotations,
    AccessMod AccessMod,
    InheritanceMod InheritanceMod,
    MemberMod MemberMods,
    AeroType Type,
    string Name,
    PropertyAccessorNode? Getter, // Null if write-only
    PropertyAccessorNode? Setter, // Null if read-only
    ExpressionNode? Initializer,
    SourceSpan Span
) : DeclarationNode(Span);

public record VariableDeclarationNode
(
    ValueList<AnnotationNode> Annotations,
    AccessMod AccessMod,
    InheritanceMod InheritanceMod,
    MemberMod MemberMods,
    VariableKind VarKind,
    AeroType Type,
    string Name,
    ExpressionNode? Initializer,
    SourceSpan Span
) : DeclarationNode(Span);

public record PrimaryConstructorDeclarationNode
(
    ValueList<AnnotationNode> Annotations,
    AccessMod AccessMod,
    ValueList<VariableDeclarationNode> Variables,
    ValueList<ParamNode> Parameters,
    BlockExpressionNode Body,
    SourceSpan Span
) : ConstructorDeclarationNode(Annotations, AccessMod, Parameters, Body, Span);

public record ConstructorDeclarationNode
(
    ValueList<AnnotationNode> Annotations,
    AccessMod AccessMod,
    ValueList<ParamNode> Parameters,
    BlockExpressionNode? Body,
    SourceSpan Span
) : DeclarationNode(Span);

public record DestructorDeclarationNode
(
    ValueList<AnnotationNode> Annotations,
    BlockExpressionNode? Body,
    SourceSpan Span
) : DeclarationNode(Span);

public record ModuleDeclarationNode
(
    string ModulePath,
    DeclarationNode[] Declarations, 
    SourceSpan Span
) : DeclarationNode(Span);