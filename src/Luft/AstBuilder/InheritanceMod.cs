namespace Luft.AstBuilder;

[Flags]
internal enum InheritanceMod
{
    None = 0,
    Virtual,    // Method can be overridden
    Abstract,   // Pure virtual, no body allowed
    Sealed,     // Overrides but prevents further overriding
    Implements  // Implements an abstract/virtual
}