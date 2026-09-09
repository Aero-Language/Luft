using Luft.Utility;

namespace Luft.TypeChecker.Symbols;

public record SymbolSignature(string Name, ValueList<GenericParameterType> Generics, ValueList<ParamSymbol> Parameters)
{
    public virtual bool Equals(SymbolSignature? other)
    {
        if (other is null) return false;
        if (other.Name != Name) return false;
        
        if (!other.Generics.Cast<AeroType>().Equals(Generics.Cast<AeroType>())) return false;
        if (!other.Parameters.Select(p => p.Type).Equals(Parameters.Select(p => p.Type))) return false;
        
        return true;
    }

    public override int GetHashCode() => Name.GetHashCode() ^ Generics.GetHashCode() ^ Parameters.GetHashCode();
}