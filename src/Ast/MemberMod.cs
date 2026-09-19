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
    public static string AsString(this MemberMod mod)
    {
        List<string> mods = [];
        
        if (mod.HasFlag(MemberMod.None))    return "";
        if (mod.HasFlag(MemberMod.Static))  mods.Add("static");
        if (mod.HasFlag(MemberMod.Weak))    mods.Add("weak");
        if (mod.HasFlag(MemberMod.Partial)) mods.Add("partial");
        if (mod.HasFlag(MemberMod.Unsafe))  mods.Add("unsafe");
        
        return string.Join(" ", mods);
    }
}