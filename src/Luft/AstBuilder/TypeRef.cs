namespace Luft.AstBuilder;


internal record TypeRef(string Name, bool IsRef, bool IsNullable, TypeRef? ElementType = null, ValueList<TypeRef>? TypeArguments = null)
{
    public bool IsArray => ElementType is not null;
    public bool IsGeneric => TypeArguments is { Count: > 0 };
    
    /// <summary>
    /// Gets the core scalar type name unwrapping all array dimensions.
    /// e.g., "int[][]" returns "int".
    /// </summary>
    public string BaseName => ElementType?.BaseName ?? Name;
    
    /// <summary>
    /// Calculates the array nesting depth.
    /// e.g., "int" = 0, "int[]" = 1, "int[][]" = 2.
    /// </summary>
    public int ArrayRank => ElementType is null ? 0 : 1 + ElementType.ArrayRank;
    
    
    public override string ToString()
    {
        if (IsArray) 
            return $"{ElementType}[]";
        
        var baseStr = Name;
        if (IsGeneric) 
            baseStr += $"<{string.Join(", ", TypeArguments!)}>";
        
        if (IsNullable) 
            baseStr += "?";
        
        if (IsRef) 
            baseStr = $"ref {baseStr}";
        
        return baseStr;
    }
}