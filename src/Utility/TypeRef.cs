using Luft.Utility;

namespace Luft.AstBuilder;


public record TypeRef(string Name, bool IsRef, bool IsNullable, bool IsAutoVar = false, TypeRef? ElementType = null, ValueList<TypeRef>? TypeArguments = null, bool IsError = false)
{
    public bool IsArray => ElementType is not null;
    public bool IsGeneric => TypeArguments is { Count: > 0 };
    
    /// <summary>
    /// Gets the core scalar type unwrapping all array dimensions.
    /// e.g., "Int[][]" returns "Int".
    /// </summary>
    public TypeRef BaseElement => ElementType?.BaseElement ?? this;
    
    /// <summary>
    /// Calculates the array nesting depth.
    /// e.g., "Int" = 0, "Int[]" = 1, "Int[][]" = 2.
    /// </summary>
    public int ArrayRank => ElementType is null ? 0 : 1 + ElementType.ArrayRank;
    
    
    public string ToString(int arraySize)
    {
        if (IsArray)
        {
            var nullStr = IsNullable ? "?" : "";
            return $"{ElementType}[{arraySize}]{nullStr}";
        }
        
        var baseStr = Name;
        if (IsGeneric) 
            baseStr += $"<{string.Join(", ", TypeArguments!)}>";
        
        if (IsNullable) 
            baseStr += "?";
        
        if (IsRef) 
            baseStr = $"ref {baseStr}";
        
        return baseStr;
    }

    public static readonly TypeRef AutoVar = new TypeRef("", false, false, IsAutoVar: true);
    public static readonly TypeRef Error = new TypeRef("", false, false, IsError: true);
    
    public static readonly TypeRef Void = new TypeRef("Void", false, false);
}