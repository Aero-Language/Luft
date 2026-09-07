namespace Luft.Ast;

public enum InheritanceMod
{
    None = 0,
    Virtual,    // Method can be overridden
    Abstract,   // Pure virtual, no body allowed
    Sealed,     // Prevents further overriding
    Implements  // Implements an abstract/virtual
}

public static class InheritanceModExtensions
{
    public static string? AsString(this InheritanceMod mod)
    {
        return mod switch
        {
            InheritanceMod.None => null,
            InheritanceMod.Abstract => "abstract",
            InheritanceMod.Virtual => "virtual",
            InheritanceMod.Implements => "impl",
            InheritanceMod.Sealed => "sealed",
            _ => throw new InvalidOperationException()
        };
    }
}