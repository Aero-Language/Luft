namespace Luft.Ast;

public enum PropertyAccessorKind
{
    Get,
    Set,
    Init
}

public static  class PropertyAccessorKindExtensions
{
    public static PropertyAccessorKind? GetAccessorKind(this string str)
    {
        return str switch
        {
            "get" => PropertyAccessorKind.Get,
            "set" => PropertyAccessorKind.Set,
            "init" => PropertyAccessorKind.Init,
            _ => null
        };
    }

    public static string AsString(this PropertyAccessorKind kind)
    {
        return kind switch
        {
            PropertyAccessorKind.Get => "get",
            PropertyAccessorKind.Set => "set",
            PropertyAccessorKind.Init => "init",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }
}