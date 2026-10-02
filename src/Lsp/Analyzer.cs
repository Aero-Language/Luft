using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker;
using Luft.Utility;

namespace Luft.Lsp;

public sealed record Hit(string Kind, string Name, string Detail, SourceSpan? Decl, SourceSpan Ref);

public sealed record Suggestion(string Label, string Kind, string Detail);

// Name based symbol lookup over the last good type table and the current syntax tree
public sealed class Analyzer(Snapshot snap)
{
    sealed record Ctx(List<LocalVar> Locals, List<string> Types);

    static readonly string[] Keywords =
    [
        "val", "var", "const", "public", "internal", "protected", "private", "static", "weak", "partial", "unsafe",
        "virtual", "abstract", "sealed", "impl", "struct", "record", "class", "fun", "enum", "trait", "extension",
        "extensions", "annotation", "constructor", "destructor", "op", "if", "else", "match", "case", "while", "for",
        "in", "break", "continue", "module", "import", "from", "return", "yield", "ref", "concurrent", "spawn",
        "get", "set", "init", "is", "not", "and", "or", "true", "false", "null", "self", "it"
    ];

    SymbolIndex Index => snap.Index;

    static List<Token> Significant(ParsedFile file) => file.Tokens
        .Where(t => t.Type is not (TokenType.Whitespace or TokenType.Comment or TokenType.Unknown or TokenType.Eof))
        .ToList();

    Ctx ContextAt(ParsedFile file, TextLocation pos)
    {
        var chain = AstNav.ChainAt(file.Ast, pos);
        return new Ctx(AstNav.LocalsAt(chain, pos), AstNav.TypeNamesOf(chain));
    }

    public Hit? Resolve(string path, TextLocation pos)
    {
        if (!snap.Files.TryGetValue(path, out var file)) return null;

        var sig = Significant(file);
        var i = sig.FindIndex(t => t.Span.Contains(pos));
        if (i < 0 || sig[i].Type is not (TokenType.Identifier or TokenType.SelfLiteral)) return null;

        var tok = sig[i];
        var name = tok.Value;
        var ctx = ContextAt(file, pos);

        if (tok.Type is TokenType.SelfLiteral)
        {
            var self = ctx.Types.LastOrDefault();
            return self is null ? null : new Hit("self", name, $"self: {self}", Index.TypeDecls.GetValueOrDefault(self)?.Span, tok.Span);
        }

        if (i > 0 && sig[i - 1].Type is TokenType.Dot)
        {
            var owner = TypeNameAt(sig, i - 2, ctx, 0);
            var members = owner is null
                ? Index.All.Where(d => d.Container is not null && d.Name == name)
                : Index.MembersOf(owner).Where(d => d.Name == name);
            var member = members.FirstOrDefault();
            return member is null ? null : FromDecl(member, tok.Span);
        }

        var local = ctx.Locals.LastOrDefault(l => l.Name == name);
        if (local is not null) return new Hit(local.Kind, name, LocalDetail(local, ctx), local.Span, tok.Span);

        var decls = Index.Named(name).ToList();
        var found = decls.FirstOrDefault(d => d.Container is not null && ctx.Types.Contains(d.Container))
                    ?? decls.FirstOrDefault(d => d.Container is null)
                    ?? decls.FirstOrDefault();
        if (found is not null) return FromDecl(found, tok.Span);

        if (TypeTable.PrimitiveTypes.Any(p => p.Name == name)) return new Hit("type", name, $"builtin type {name}", null, tok.Span);
        return null;
    }

    // Narrows a declaration span (which starts at the keyword) down to the name token
    public SourceSpan NameSpan(SourceSpan decl, string name)
    {
        if (!snap.Files.TryGetValue(decl.FilePath, out var file)) return decl;
        var last = name.Split('.')[^1];
        var tok = file.Tokens.FirstOrDefault(t => t.Type is TokenType.Identifier && t.Value == last && t.Span.Start >= decl.Start);
        return tok?.Span ?? decl;
    }

    public List<Suggestion> Complete(string path, TextLocation pos)
    {
        var result = new List<Suggestion>();
        if (!snap.Files.TryGetValue(path, out var file)) return result;

        var sig = Significant(file);
        var prefix = sig.FindIndex(t => t.Type is TokenType.Identifier && t.Span.Start < pos && pos <= t.Span.End);
        var before = prefix >= 0 ? prefix - 1 : sig.FindLastIndex(t => t.Span.End <= pos);
        var ctx = ContextAt(file, pos);

        if (before >= 0 && sig[before].Type is TokenType.Dot)
        {
            var recv = before - 1;
            var owner = TypeNameAt(sig, recv, ctx, 0);
            if (owner is null) return result;

            var isStaticRef = recv >= 0 && sig[recv].Type is TokenType.Identifier
                              && !ctx.Locals.Any(l => l.Name == sig[recv].Value)
                              && Index.TypeNames.Contains(sig[recv].Value);

            foreach (var d in Index.MembersOf(owner))
                if (isStaticRef ? d.IsStatic : !d.IsStatic) result.Add(new Suggestion(d.Name, d.Kind, d.Detail));
            return result.DistinctBy(s => (s.Label, s.Detail)).ToList();
        }

        foreach (var k in Keywords) result.Add(new Suggestion(k, "keyword", ""));
        foreach (var p in TypeTable.PrimitiveTypes) result.Add(new Suggestion(p.Name, "type", "builtin type"));
        foreach (var l in ctx.Locals) result.Add(new Suggestion(l.Name, l.Kind, LocalDetail(l, ctx)));

        foreach (var d in Index.All.Where(d => d.Container is null))
            result.Add(new Suggestion(d.Name, d.Kind, d.Detail));

        foreach (var type in ctx.Types)
            foreach (var d in Index.MembersOf(type))
                result.Add(new Suggestion(d.Name, d.Kind, d.Detail));

        return result.DistinctBy(s => (s.Label, s.Kind, s.Detail)).ToList();
    }

    static Hit FromDecl(Decl d, SourceSpan reference) => new(d.Kind, d.Name, d.Detail, d.Span, reference);

    string LocalDetail(LocalVar l, Ctx ctx)
    {
        var type = !l.Type.IsAuto ? l.Type.ToString() : LocalTypeName(l, ctx, 0) ?? "<inferred>";
        return l.Kind == "parameter" ? $"(parameter) {l.Name}: {type}" : $"{l.Kind} {l.Name}: {type}";
    }

    string? LocalTypeName(LocalVar l, Ctx ctx, int depth)
        => !l.Type.IsAuto ? SymbolIndex.TypeName(l.Type) : InferName(l.Init, ctx, depth + 1);

    static string? LiteralType(TokenType t) => t switch
    {
        TokenType.IntLiteral => "Int",
        TokenType.FloatLiteral => "Float",
        TokenType.CharLiteral => "Char",
        TokenType.StringLiteral => "String",
        TokenType.BooleanLiteral => "Bool",
        _ => null
    };

    // Best-effort type of an initializer; null when it can't be told without the full checker
    string? InferName(ExpressionNode? e, Ctx ctx, int depth)
    {
        if (depth > 8) return null;

        return e switch
        {
            LiteralExpressionNode l => LiteralType(l.LiteralType),
            StringInterpolationExpressionNode => "String",
            ScopedExpressionNode s => InferName(s.Scoped, ctx, depth + 1),
            CallExpressionNode { Target: IdentifierExpressionNode id } => CalleeType(id.Name),
            IdentifierExpressionNode id => ctx.Locals.LastOrDefault(l => l.Name == id.Name) is { } lv ? LocalTypeName(lv, ctx, depth) : null,
            _ => null
        };
    }

    string? CalleeType(string name)
        => Index.TypeNames.Contains(name)
            ? name
            : SymbolIndex.TypeName(Index.Named(name).FirstOrDefault(d => d.Kind is "function" or "method")?.Type);

    // Type name of the expression that ends at sig[j]; handles names, member chains, literals, self and simple calls
    string? TypeNameAt(List<Token> sig, int j, Ctx ctx, int depth)
    {
        if (j < 0 || depth > 8) return null;
        var t = sig[j];

        switch (t.Type)
        {
            case TokenType.SelfLiteral: return ctx.Types.LastOrDefault();
            case TokenType.ParenthesisClose: return CallTypeAt(sig, j, ctx, depth);
            case TokenType.Identifier: break;
            default: return LiteralType(t.Type);
        }

        var name = t.Value;
        if (j > 0 && sig[j - 1].Type is TokenType.Dot)
        {
            var owner = TypeNameAt(sig, j - 2, ctx, depth + 1);
            var member = owner is null ? null : Index.MembersOf(owner).FirstOrDefault(d => d.Name == name);
            return SymbolIndex.TypeName(member?.Type);
        }

        var local = ctx.Locals.LastOrDefault(l => l.Name == name);
        if (local is not null) return LocalTypeName(local, ctx, depth);
        if (Index.TypeNames.Contains(name)) return name;

        var decl = Index.Named(name).FirstOrDefault(d => d.Container is null || ctx.Types.Contains(d.Container));
        return SymbolIndex.TypeName(decl?.Type);
    }

    string? CallTypeAt(List<Token> sig, int close, Ctx ctx, int depth)
    {
        int nesting = 0, open = close;
        for (; open >= 0; open--)
        {
            if (sig[open].Type is TokenType.ParenthesisClose) nesting++;
            else if (sig[open].Type is TokenType.ParenthesisOpen && --nesting == 0) break;
        }
        if (open <= 0 || sig[open - 1].Type is not TokenType.Identifier) return null;

        var callee = sig[open - 1].Value;
        if (open >= 2 && sig[open - 2].Type is TokenType.Dot)
        {
            var owner = TypeNameAt(sig, open - 3, ctx, depth + 1);
            var fn = owner is null ? null : Index.MembersOf(owner).FirstOrDefault(d => d.Name == callee && d.Kind is "method" or "extension fun");
            return SymbolIndex.TypeName(fn?.Type);
        }

        return CalleeType(callee);
    }
}