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
                var arguments = g.TypeArguments.Select(p => map.TryGetValue(p.Name, out var bound)
                    ? (bound is ScalarType b ? new GenericParameterType(b.Name, null, b.IsRef || p.IsRef, b.IsNullable || p.IsNullable) : new GenericParameterType(bound.ToString()))
                    : p).ToValueList();
                return new GenericType(g.Definition, arguments);
            case LambdaType l:
                return new LambdaType(l.Parameters.Select(p => new TypeParam(p.Name, Apply(p.Type, map))).ToValueList(), Apply(l.ReturnType, map), l.IsRef, l.IsNullable);
            default:
                return type;
        }
    }

    private static AeroType WithFlags(AeroType type, bool isRef, bool isNullable)
    {
        if (!isRef && !isNullable) return type;

        return type switch
        {
            ScalarType s => s with { IsRef = s.IsRef || isRef, IsNullable = s.IsNullable || isNullable },
            ArrayType a => new ArrayType(a.ElementType, a.IsRef || isRef, a.IsNullable || isNullable),
            LambdaType l => new LambdaType(l.Parameters, l.ReturnType, l.IsRef || isRef, l.IsNullable || isNullable),
            _ => type
        };
    }

    // One entry per generic parameter, in declaration order
    public static string Key(IEnumerable<GenericParameterType> parameters, IReadOnlyDictionary<string, AeroType> map)
        => string.Join(",", parameters.Select(p => map[p.Name]));
}
