namespace Luft.AstBuilder;

[Flags]
public enum MemberMod
{
    None = 0,
    Static,
    Weak,
    Partial,
    Unsafe
}