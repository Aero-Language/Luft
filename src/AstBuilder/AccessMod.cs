namespace Luft.AstBuilder;

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
    public static AccessMod ClassDeclDefault => AccessMod.Internal;
    public static AccessMod StructDeclDefault => AccessMod.Internal;
    public static AccessMod VariableDefault => AccessMod.Private;
    public static AccessMod PropertyDefault => AccessMod.Public;
}