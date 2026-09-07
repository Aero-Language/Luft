namespace Luft.Utility;

// New
public abstract record AeroType(string Name, bool IsRef = false, bool IsNullable = false)
{
    public bool IsArray => this is ArrayType;
    public bool IsGeneric => this is GenericType;
    public bool IsLambda => this is LambdaType;
    public bool IsAuto => this == Auto;
    public bool IsError => this == Error;
    
    // Well-known core types
    public static readonly AeroType Void = new ScalarType("Void");
    public static readonly AeroType Int = new ScalarType("Int");
    public static readonly AeroType Float = new ScalarType("Float");
    public static readonly AeroType Char = new ScalarType("Char");
    public static readonly AeroType String = new ScalarType("String");
    public static readonly AeroType Bool = new ScalarType("Bool");
    public static readonly AeroType Byte = new ScalarType("Byte");

    public static readonly AeroType Auto = new SpecialType("<auto>");
    public static readonly AeroType Error = new SpecialType("<error>");
}

public sealed record ScalarType(string Name, bool IsRef = false, bool IsNullable = false)
    : AeroType(Name, IsRef, IsNullable)
{
    public override string ToString() => $"{(IsRef ? "ref " : "")}{Name}{(IsNullable ? "?" : "")}";
}

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

public sealed record SpecialType(string typeName) : AeroType(typeName, false, false)
{
    public override string ToString() => Name;
}