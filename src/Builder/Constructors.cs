using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

// Explicit is a `constructor` declaration, Primary the `Type(var X: Int)` form, Empty the implicit one without parameters, Fields a record's implicit one taking every field
public enum ConstructorKind { Explicit, Primary, Empty, Fields }

public readonly record struct ConstructorKey(TypeSymbol Type, ConstructorSymbol? Symbol, ConstructorKind Kind);

// The constructors a type offers, in the same shapes BodyResolver matches calls against
public static class ConstructorShapes
{
    // `arguments` are the type arguments when the type is an instance of a generic one
    // `layout` is null for the opaque classes of a library, which are only known by their declarations
    public static List<(ConstructorKey Key, ValueList<ParamSymbol> Parameters)> Of(TypeSymbol type, TypeLayout? layout, IReadOnlyDictionary<string, AeroType>? arguments = null)
    {
        var result = new List<(ConstructorKey Key, ValueList<ParamSymbol> Parameters)>();

        foreach (var c in type.Scope.Constructors)
            result.Add((new ConstructorKey(type, c, ConstructorKind.Explicit), Substitute(c.Parameters, arguments)));

        if (type.Scope.PrimaryConstructor is { } primary)
        {
            var parameters = primary.Variables.Select(v => Parameter(v.Name, v.Type, v.VarKind, v.Initializer, layout)).ToValueList();
            result.Add((new ConstructorKey(type, null, ConstructorKind.Primary), parameters));
        }

        if (result.Count > 0) return result;

        result.Add((new ConstructorKey(type, null, ConstructorKind.Empty), ValueList<ParamSymbol>.Empty));
        if (type is RecordSymbol)
        {
            var fields = type.Scope.Fields.Values.SelectMany(f => f)
                .Where(f => !f.Declaration.MemberMods.HasFlag(MemberMod.Static) && f.VarKind != VariableKind.Const)
                .Select(f => Parameter(f.Name, f.Type, f.VarKind, f.Initializer, layout))
                .ToValueList();
            if (fields.Count > 0) result.Add((new ConstructorKey(type, null, ConstructorKind.Fields), fields));
        }

        return result;
    }

    private static ValueList<ParamSymbol> Substitute(ValueList<ParamSymbol> parameters, IReadOnlyDictionary<string, AeroType>? arguments)
        => arguments is null ? parameters : parameters.Select(p => p with { Type = TypeSubstitution.Apply(p.Type, arguments) }).ToValueList();

    // Which constructor a call bound to; the checker only records the symbol of explicit ones
    public static ConstructorKey Select(TypeSymbol type, ConstructorSymbol? symbol, int argumentCount)
    {
        if (symbol is not null) return new ConstructorKey(type, symbol, ConstructorKind.Explicit);
        if (type.Scope.PrimaryConstructor is not null) return new ConstructorKey(type, null, ConstructorKind.Primary);

        return new ConstructorKey(type, null, argumentCount == 0 ? ConstructorKind.Empty : ConstructorKind.Fields);
    }

    // A parameter that becomes a field takes the field's resolved type, so an inferred one is never Auto
    private static ParamSymbol Parameter(string name, AeroType type, VariableKind kind, ExpressionNode? initializer, TypeLayout? layout)
        => new(name, layout?.Find(name)?.Type ?? type, kind, initializer);
}
