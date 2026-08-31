using System.Security;

namespace Luft.Utility;


// Old
/*public record AeroType(string Name, bool IsRef, bool IsNullable, bool IsAutoVar = false, AeroType? ElementType = null, ValueList<AeroType>? TypeArguments = null, bool IsError = false)
{
    public bool IsArray => ElementType is not null;
    public bool IsGeneric => TypeArguments is { Count: > 0 };
    
    /// <summary>
    /// Gets the core scalar type unwrapping all array dimensions.
    /// e.g., "Int[][]" returns "Int".
    /// </summary>
    public AeroType BaseElement => ElementType?.BaseElement ?? this;
    
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

    public static readonly AeroType AutoVar = new AeroType("", false, false, IsAutoVar: true);
    public static readonly AeroType Error = new AeroType("", false, false, IsError: true);
    
    public static readonly AeroType Void = new AeroType("Void", false, false); 
}*/


// New
public abstract record AeroType(string Name, bool IsRef = false, bool IsNullable = false)
{
    public bool IsArray => this is ArrayType;
    public bool IsGeneric => this is GenericType;
    public bool IsLambda => this is LambdaType;
    public bool IsAuto => this is AutoType;
    public bool IsError => this is ErrorType;

    public override string ToString() => $"{(IsRef ? "ref " : "")}{Name}{(IsNullable ? "?" : "")}";

    // Well-known core types
    public static readonly AeroType Void = new ScalarType("Void");
    public static readonly AeroType Int = new ScalarType("Int");
    public static readonly AeroType Float = new ScalarType("Float");
    public static readonly AeroType Char = new ScalarType("Char");
    public static readonly AeroType String = new ScalarType("String");
    public static readonly AeroType Bool = new ScalarType("Bool");
    public static readonly AeroType Byte = new ScalarType("Byte");

    public static readonly AeroType Auto = new AutoType();
    public static readonly AeroType Error = new ErrorType();
}

public sealed record ScalarType(string Name, bool IsRef = false, bool IsNullable = false) 
    : AeroType(Name, IsRef, IsNullable);

public sealed record ArrayType(AeroType ElementType, bool IsRef = false, bool IsNullable = false) 
    : AeroType($"{ElementType}[]", IsRef, IsNullable)
{
    public AeroType BaseElement => ElementType is ArrayType array ? array.BaseElement : ElementType;
    public int ArrayRank => ElementType is ArrayType array ? 1 + array.ArrayRank : 1;
    public override string ToString() => $"{(IsRef ? "ref " : "")}{ElementType}[]{(IsNullable ? "?" : "")}";
}

/// <summary>
/// Represents an applied generic type like List&lt;Int&gt; or Map&lt;String, Int&gt;.
/// </summary>
public sealed record GenericType(AeroType Definition, ValueList<GenericParameterType> TypeArguments) 
    : AeroType(Definition.Name, Definition.IsRef, Definition.IsNullable)
{
    public override string ToString() => $"{(IsRef ? "ref " : "")}{Name}<{string.Join(", ", TypeArguments)}>{(IsNullable ? "?" : "")}";
}

/// <summary>
/// Represents a generic parameter placeholder with an optional constraint, e.g., T : Comparable.
/// </summary>
public sealed record GenericParameterType(string ParameterName, AeroType? Constraint = null, bool IsRef = false, bool IsNullable = false) 
    : AeroType(ParameterName, IsRef, IsNullable)
{
    public override string ToString() => Constraint is null ? Name : $"{Name}: {Constraint}";
}

public sealed record LambdaType(ValueList<TypeParam> Parameters, AeroType ReturnType, bool IsRef = false, bool IsNullable = false) 
    : AeroType("", IsRef, IsNullable)
{
    public override string ToString() => $"({string.Join(", ", Parameters)}) -> {ReturnType}";
}

public sealed record TypeParam(string Name, AeroType Type)
{
    public override string ToString() => $"{Name}: {Type}";
}

public sealed record ErrorType() : AeroType("<error>", false, false);
public sealed record AutoType() : AeroType("<auto>", false, false);