namespace Luft.Parser.AstBuilder;

[Flags]
public enum MemberMod
{
    None,
    Static,
    Abstract,
    Sealed,
    Weak
}