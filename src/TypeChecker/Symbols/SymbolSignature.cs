using Luft.Ast;
using Luft.Utility;

namespace Luft.TypeChecker.Symbols;

public record SymbolSignature(string Name, ValueList<GenericParameterType> Generics, ValueList<ParamSymbol> Parameters)
{
    public virtual bool Equals(SymbolSignature? other)
    {
        if (other is null) return false;
        if (other.Name != Name) return false;
        
        if (other.Generics.Count != Generics.Count) return false; // Just a simple count check here
        if (!other.Parameters.Select(p => p.Type).SequenceEqual(Parameters.Select(p => p.Type))) return false;
        
        return true;
    }

    public override int GetHashCode() => Name.GetHashCode() ^ Generics.GetHashCode() ^ Parameters.GetHashCode();
}

public static class AeroTypeExtensions
{
    extension(AeroType type)
    {
        public SymbolSignature Signature
        {
            get
            {
                return type switch
                {
                    LambdaType l => new(l.Name, [], l.Parameters.Select(p => new ParamSymbol(p.Name, p.Type, VariableKind.Val, null)).ToValueList()),
                    GenericType g => new SymbolSignature(g.Definition.Name, g.TypeArguments, []),
                    _ => new(type.Name, [], [])
                };
            }
        }
    }
}