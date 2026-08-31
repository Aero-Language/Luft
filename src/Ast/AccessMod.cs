namespace Luft.Ast;

public enum AccessMod
{
    Public,
    Internal,
    Protected,
    Private
}

public static class AccessModExtensions
{
    public static AccessMod FunctionDefault => AccessMod.Private;
    public static AccessMod ExtensionDefault => AccessMod.Internal;
    public static AccessMod ClassDeclDefault => AccessMod.Internal;
    public static AccessMod StructDeclDefault => AccessMod.Internal;
    public static AccessMod TraitDeclDefault => AccessMod.Internal;
    public static AccessMod EnumDeclDefault => AccessMod.Internal;
    public static AccessMod RecordDeclDefault => AccessMod.Internal;
    public static AccessMod AnnotationDeclDefault => AccessMod.Internal;
    public static AccessMod VariableDefault => AccessMod.Private;
    public static AccessMod PropertyDefault => AccessMod.Public;
}