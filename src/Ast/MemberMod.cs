namespace Luft.Ast;

[Flags]
public enum MemberMod
{
    None = 0,
    Static,
    Weak,
    Partial,
    Unsafe
}

public static class MemberModExtensions
{
    public static string? AsString(this MemberMod mod)
    {
        if (mod == MemberMod.None) return null;
        
        List<string> mods = [];

        switch (mod)
        {
            case MemberMod.Partial: mods.Add("partial"); break;
            case MemberMod.Static: mods.Add("static"); break;
            case MemberMod.Unsafe: mods.Add("unsafe"); break;
        }
        
        return string.Join(" ", mods);
    }
}