using Luft.Utility;

namespace Luft.Builder;

// Replaces generic parameter names by the types they were bound to
public static class TypeSubstitution
{
    public static AeroType Apply(AeroType type, IReadOnlyDictionary<string, AeroType>? map)
    {
        if (map is null || map.Count == 0) return type;

        switch (type)
        {
            case ScalarType s when map.TryGetValue(s.Name, out var bound):
                return WithFlags(bound, s.IsRef, s.IsNullable);
            case GenericParameterType p when map.TryGetValue(p.Name, out var bound):
                return WithFlags(bound, p.IsRef, p.IsNullable);
            case ArrayType a:
                return new ArrayType(Apply(a.ElementType, map), a.IsRef, a.IsNullable);
            case GenericType g:
                var arguments = g.TypeArguments.Select(p => TypeText.Parse(p) is { } parsed ? TypeText.ToArgument(Apply(parsed, map)) : p).ToValueList();
                return new GenericType(g.Definition, arguments);
            case LambdaType l:
                return new LambdaType(l.Parameters.Select(p => new TypeParam(p.Name, Apply(p.Type, map))).ToValueList(), Apply(l.ReturnType, map), l.IsRef, l.IsNullable);
            default:
                return type;
        }
    }

    public static AeroType WithFlags(AeroType type, bool isRef, bool isNullable)
    {
        if (!isRef && !isNullable) return type;

        return type switch
        {
            ScalarType s => s with { IsRef = s.IsRef || isRef, IsNullable = s.IsNullable || isNullable },
            ArrayType a => new ArrayType(a.ElementType, a.IsRef || isRef, a.IsNullable || isNullable),
            LambdaType l => new LambdaType(l.Parameters, l.ReturnType, l.IsRef || isRef, l.IsNullable || isNullable),
            GenericType g => new GenericType(WithFlags(g.Definition, isRef, isNullable), g.TypeArguments),
            _ => type
        };
    }

    // Both sets of entries, the second one wins; null when there are none
    public static Dictionary<string, T>? Merge<T>(IReadOnlyDictionary<string, T>? first, IReadOnlyDictionary<string, T>? second)
    {
        if (first is null && second is null) return null;

        var merged = new Dictionary<string, T>();
        if (first is not null) foreach (var (k, v) in first) merged[k] = v;
        if (second is not null) foreach (var (k, v) in second) merged[k] = v;
        return merged;
    }

    // One entry per generic parameter, in declaration order
    public static string Key(IEnumerable<GenericParameterType> parameters, IReadOnlyDictionary<string, AeroType> map)
        => string.Join(",", parameters.Select(p => map[p.Name]));
}
