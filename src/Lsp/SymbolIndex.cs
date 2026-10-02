using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Lsp;

public sealed record Decl(string Name, string Kind, string Detail, SourceSpan Span, string? Container, bool IsStatic, AeroType? Type);

// Flat, name-based view of everything the type table declares
public sealed class SymbolIndex
{
    public static readonly SymbolIndex Empty = new();

    public List<Decl> All { get; } = [];
    public Dictionary<string, Decl> TypeDecls { get; } = new();
    public Dictionary<string, List<string>> Bases { get; } = new();
    public HashSet<string> TypeNames { get; } = new();

    public static SymbolIndex Build(TypeTable table)
    {
        var index = new SymbolIndex();
        foreach (var module in table.Modules.Values) index.AddScope(module.Scope, null);
        return index;
    }

    public IEnumerable<Decl> Named(string name) => All.Where(d => d.Name == name);

    // Members of a type including those inherited from bases
    public IEnumerable<Decl> MembersOf(string type, HashSet<string>? seen = null)
    {
        seen ??= new HashSet<string>();
        if (!seen.Add(type)) yield break;

        foreach (var d in All)
            if (d.Container == type) yield return d;

        if (Bases.TryGetValue(type, out var bases))
            foreach (var b in bases)
                foreach (var d in MembersOf(b, seen))
                    yield return d;
    }

    public static string? TypeName(AeroType? t) => t switch
    {
        ScalarType s => s.Name,
        GenericType g => g.Definition.Name,
        _ => null
    };

    static string Show(AeroType t) => t.IsAuto ? "<inferred>" : t.ToString();
    static string Last(string name) => name.Split('.')[^1];

    static string Sig(FunctionSymbol f)
    {
        var generics = f.GenericParameters.Count > 0 ? $"<{string.Join(", ", f.GenericParameters)}>" : "";
        var ps = string.Join(", ", f.Parameters.Select(p => $"{p.Name}: {Show(p.Type)}"));
        return $"{f.Access.AsString()} fun {f.Name}{generics}({ps}) -> {Show(f.ReturnType)}";
    }

    void AddScope(TypeScope scope, string? container)
    {
        foreach (var type in scope.Types.Values.SelectMany(l => l)) AddType(type, container);

        foreach (var f in scope.Functions.Values.SelectMany(l => l))
            All.Add(new Decl(f.Name, container is null ? "function" : "method", Sig(f), f.Span, container, f.MemberMods.HasFlag(MemberMod.Static), f.ReturnType));

        foreach (var f in scope.ExtensionFunctions.Values.SelectMany(l => l))
            All.Add(new Decl(Last(f.Name), "extension fun", "extension " + Sig(f), f.Span, TypeName(f.ExtensionTarget), false, f.ReturnType));

        foreach (var p in scope.Properties.Values.SelectMany(l => l))
            All.Add(new Decl(p.Name, "property", $"{p.Name}: {Show(p.Type)}", p.Span, container, p.Declaration.MemberMods.HasFlag(MemberMod.Static), p.Type));

        foreach (var p in scope.ExtensionProperties.Values.SelectMany(l => l))
            All.Add(new Decl(Last(p.Name), "property", $"extension {p.Name}: {Show(p.Type)}", p.Span, TypeName(p.ExtensionTarget), false, p.Type));

        foreach (var f in scope.Fields.Values.SelectMany(l => l))
        {
            var isConst = f.VarKind == VariableKind.Const;
            All.Add(new Decl(f.Name, isConst ? "constant" : "field", $"{f.VarKind.AsString()} {f.Name}: {Show(f.Type)}", f.Span, container,
                isConst || f.Declaration.MemberMods.HasFlag(MemberMod.Static), f.Type));
        }
    }

    void AddType(TypeSymbol t, string? container)
    {
        string kind;
        IEnumerable<AeroType> bases = Enumerable.Empty<AeroType>();
        switch (t)
        {
            case ClassSymbol c: kind = "class"; bases = c.Node.Implements; break;
            case StructSymbol s: kind = "struct"; bases = s.Node.Implements; break;
            case RecordSymbol r: kind = "record"; bases = r.Node.Implements; break;
            case TraitSymbol tr: kind = "trait"; bases = tr.Node.Traits; break;
            case EnumSymbol e: kind = e.IsEnumClass ? "enum class" : "enum"; break;
            case AnnotationSymbol: kind = "annotation"; break;
            default: kind = "type"; break;
        }

        var generics = t.GenericParameters.Count > 0 ? $"<{string.Join(", ", t.GenericParameters)}>" : "";
        var decl = new Decl(t.Name, kind, $"{t.Access.AsString()} {kind} {t.Name}{generics}", t.Span, container, true, new ScalarType(t.Name));
        All.Add(decl);
        TypeDecls[t.Name] = decl;
        TypeNames.Add(t.Name);
        Bases[t.Name] = bases.Select(TypeName).OfType<string>().ToList();

        if (t is EnumSymbol en)
        {
            foreach (var m in en.Members)
                All.Add(new Decl(m.Name, "enum member", $"{en.Name}.{m.Name}", m.Declaration.Span, en.Name, true, new ScalarType(en.Name)));
            foreach (var p in en.Parameters)
                All.Add(new Decl(p.Name, "property", $"{p.Name}: {Show(p.Type)}", en.Span, en.Name, false, p.Type));
        }

        AddScope(t.Scope, t.Name);
    }
}