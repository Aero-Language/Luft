namespace Luft.AstBuilder;

public enum InheritanceMod
{
    None = 0,
    Virtual,    // Method can be overridden
    Abstract,   // Pure virtual, no body allowed
    Sealed,     // Prevents further overriding
    Implements  // Implements an abstract/virtual
}