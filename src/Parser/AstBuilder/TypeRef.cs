using Luft.Parser.AstBuilder.Ast;

namespace Luft.Parser.AstBuilder;


public record TypeRef(string Name, bool IsRef, bool IsNullable, bool IsAutoVar = false, TypeRef? ElementType = null, ExpressionNode? ArraySize = null, ValueList<TypeRef>? TypeArguments = null, bool IsError = false)
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
    
    
    public override string ToString()
    {
        if (IsArray)
        {
            var sizeStr = ArraySize != null ? ArraySize.ToString() : "";
            var nullStr = IsNullable ? "?" : "";
            return $"{ElementType}[{sizeStr}]{nullStr}";
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

    public static readonly TypeRef AutoVar = new TypeRef("", false, false, true);
    public static readonly TypeRef Error = new TypeRef("", false, false, IsError: true);
}