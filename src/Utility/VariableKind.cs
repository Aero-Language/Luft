namespace Luft.Ast;

public enum VariableKind
{
    Val,
    Var,
    Const
}

public static class VariableKindExtensions
{
    public static string AsString(this VariableKind kind)
    {
        return kind switch
        {
            VariableKind.Val => "val",
            VariableKind.Var => "var",
            VariableKind.Const => "const",
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }
}