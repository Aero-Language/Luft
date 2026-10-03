using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker;
using Luft.Utility;

namespace Luft.Lsp;

public sealed record Hit(string Kind, string Name, string Detail, SourceSpan? Decl, SourceSpan Ref);

// Rank orders the list: lower comes first
public sealed record Suggestion(string Label, string Kind, string Detail, int Rank = 3);

public sealed record SemanticTok(SourceSpan Span, string Type);

// Name based symbol lookup over the last good type table and the current syntax tree
public sealed class Analyzer(Snapshot snap)
{
    sealed record Ctx(List<LocalVar> Locals, List<string> Types, List<string> Generics);

    enum Place { Top, TypeBody, Statement, Property, Enum }

    static readonly HashSet<string> AccessMods = ["public", "internal", "protected", "private"];
    static readonly HashSet<string> MemberMods = ["static", "weak", "partial", "unsafe"];
    static readonly HashSet<string> InheritMods = ["virtual", "abstract", "sealed", "impl"];

    static readonly string[] DeclKeywords =
    [
        "val", "var", "const", "fun", "op", "struct", "record", "class", "enum", "trait",
        "extension", "extensions", "annotation", "constructor", "destructor"
    ];

    static readonly string[] StatementKeywords =
    [
        "val", "var", "const", "if", "match", "while", "for", "break", "continue", "return", "yield",
        "concurrent", "spawn", "self", "it", "true", "false", "null"
    ];

    static readonly string[] ExprKeywords = ["self", "it", "true", "false", "null", "if", "match", "spawn", "concurrent"];

    static readonly HashSet<TokenType> ExprPrev =
    [
        TokenType.Assign, TokenType.AddAssign, TokenType.SubtractAssign, TokenType.MultiplyAssign, TokenType.DivideAssign,
        TokenType.ModuloAssign, TokenType.Equality, TokenType.Inequality, TokenType.LessThan, TokenType.GreaterThan,
        TokenType.LessThanEqual, TokenType.GreaterThanEqual, TokenType.Add, TokenType.Subtract, TokenType.Multiply,
        TokenType.Divide, TokenType.Modulo, TokenType.LogicalAnd, TokenType.LogicalOr, TokenType.And, TokenType.Or,
        TokenType.LogicalNot, TokenType.Not, TokenType.BitwiseAnd, TokenType.BitwiseOr, TokenType.BitwiseXor,
        TokenType.ParenthesisOpen, TokenType.Comma, TokenType.SquareOpen, TokenType.ReturnKeyword, TokenType.YieldKeyword,
        TokenType.EqualArrow, TokenType.RangeSymbol, TokenType.CastSymbol, TokenType.InKeyword
    ];

    SymbolIndex Index => snap.Index;

    static List<Token> Significant(ParsedFile file) => file.Tokens
        .Where(t => t.Type is not (TokenType.Whitespace or TokenType.Comment or TokenType.Unknown or TokenType.Eof))
        .ToList();

    static IEnumerable<string> GenericsOf(AstNode n) => n switch
    {
        ClassDeclarationNode c => c.GenericParameters.Select(g => g.Name),
        StructDeclarationNode s => s.GenericParameters.Select(g => g.Name),
        RecordDeclarationNode r => r.GenericParameters.Select(g => g.Name),
        TraitDeclarationNode t => t.GenericParameters.Select(g => g.Name),
        AnnotationDeclarationNode a => a.GenericParameters.Select(g => g.Name),
        FunctionDeclarationNode f => f.GenericParameters.Select(g => g.Name),
        _ => Enumerable.Empty<string>()
    };

    Ctx ContextAt(ParsedFile file, TextLocation pos)
    {
        var chain = AstNav.ChainAt(file.Ast, pos);
        return new Ctx(AstNav.LocalsAt(chain, pos), AstNav.TypeNamesOf(chain), chain.SelectMany(GenericsOf).ToList());
    }

    public Hit? Resolve(string path, TextLocation pos)
    {
        if (!snap.Files.TryGetValue(path, out var file)) return null;

        var sig = Significant(file);
        var i = sig.FindIndex(t => t.Span.Contains(pos));
        return i < 0 ? null : ResolveAt(file, sig, i);
    }

    Hit? ResolveAt(ParsedFile file, List<Token> sig, int i)
    {
        var tok = sig[i];
        if (tok.Type is not (TokenType.Identifier or TokenType.SelfLiteral)) return null;

        var name = tok.Value;
        var ctx = ContextAt(file, tok.Span.Start);

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

        if (ctx.Generics.Contains(name)) return new Hit("type parameter", name, $"type parameter {name}", null, tok.Span);

        var local = ctx.Locals.LastOrDefault(l => l.Name == name);
        if (local is not null) return new Hit(local.Kind, name, LocalDetail(local, ctx), local.Span, tok.Span);

        var decls = Index.Named(name).ToList();
        var found = decls.FirstOrDefault(d => d.Container is not null && ctx.Types.Contains(d.Container))
                    ?? decls.FirstOrDefault(d => d.Container is null)
                    ?? decls.FirstOrDefault();
        if (found is not null) return FromDecl(found, tok.Span);

        if (TypeTable.PrimitiveTypes.Any(p => p.Name == name)) return new Hit("builtin", name, $"builtin type {name}", null, tok.Span);
        return null;
    }

    // Semantic highlighting: every identifier that resolves to a type or a variable
    public List<SemanticTok> Classify(string path)
    {
        var result = new List<SemanticTok>();
        if (!snap.Files.TryGetValue(path, out var file)) return result;

        var sig = Significant(file);
        for (int i = 0; i < sig.Count; i++)
        {
            if (sig[i].Type is not TokenType.Identifier) continue;

            var hit = ResolveAt(file, sig, i);
            var type = hit is null ? null : SemanticType(hit.Kind);
            if (type is not null) result.Add(new SemanticTok(sig[i].Span, type));
        }

        return result;
    }

    static string? SemanticType(string kind) => kind switch
    {
        "class" or "annotation" or "type" => "class",
        "struct" => "struct",
        "record" => "record",
        "trait" => "interface",
        "enum" or "enum class" => "enum",
        "enum member" => "enumMember",
        "type parameter" => "typeParameter",
        "property" => "property",
        "field" => "field",
        "constant" or "const" => "constant",
        "parameter" => "parameter",
        "val" or "var" => "variable",
        _ => null
    };

    // Narrows a declaration span (which starts at the keyword) down to the name token
    public SourceSpan NameSpan(SourceSpan decl, string name)
    {
        if (!snap.Files.TryGetValue(decl.FilePath, out var file)) return decl;
        var last = name.Split('.')[^1];
        var tok = file.Tokens.FirstOrDefault(t => t.Type is TokenType.Identifier && t.Value == last && t.Span.Start >= decl.Start);
        return tok?.Span ?? decl;
    }

    static bool IsWord(Token t)
        => t.Type is not (TokenType.StringLiteral or TokenType.CharLiteral or TokenType.IntLiteral or TokenType.FloatLiteral
               or TokenType.InterpolationStart or TokenType.InterpolationEnd)
           && t.Value.Length > 0 && (char.IsLetter(t.Value[0]) || t.Value[0] == '_');

    public List<Suggestion> Complete(string path, TextLocation pos)
    {
        var result = new List<Suggestion>();
        if (!snap.Files.TryGetValue(path, out var file)) return result;

        var sig = Significant(file);
        if (sig.Any(t => t.Type is TokenType.StringLiteral or TokenType.CharLiteral && t.Span.Start < pos && pos < t.Span.End)) return result;

        // The word being typed is not context, so everything is judged from the token before it
        var prefix = sig.FindIndex(t => t.Span.Start < pos && pos <= t.Span.End && IsWord(t));
        var limit = prefix >= 0 ? prefix : sig.FindIndex(t => t.Span.Start >= pos);
        if (limit < 0) limit = sig.Count;

        var before = limit - 1;
        var prev = before >= 0 ? sig[before] : null;
        var ctx = ContextAt(file, pos);

        if (prev?.Type is TokenType.Dot) return Members(sig, before - 1, ctx);

        if (prev is not null)
        {
            if (IntroducesName(sig, before)) return result; // a new name is being typed
            if (TakesType(sig, before)) return TypeSuggestions(ctx);
            if (prev.Type is TokenType.ImportKeyword or TokenType.FromKeyword) return ImportSuggestions(sig, before);
            if (prev.Type is TokenType.InstanceKind && prev.Value == "extension")
                return [new Suggestion("fun", "keyword", "", 5), new Suggestion("op", "keyword", "", 5)];
        }

        var place = PlaceAt(sig, limit);
        if (place is Place.Enum) return result;

        var mods = ModifierRun(sig, before);
        var exprContext = prev is not null && mods.Count == 0 && ExprPrev.Contains(prev.Type);

        if (mods.Count > 0 || (!exprContext && place is Place.Top or Place.TypeBody or Place.Property))
        {
            foreach (var k in DeclarationKeywords(place, mods)) result.Add(new Suggestion(k, "keyword", "", 5));
            return result;
        }

        var keywords = exprContext ? ExprKeywords : StatementKeywords;
        foreach (var k in keywords) result.Add(new Suggestion(k, "keyword", "", 5));
        if (!exprContext && prev?.Type is TokenType.BracketClose) result.Add(new Suggestion("else", "keyword", "", 5));

        foreach (var l in ctx.Locals) result.Add(new Suggestion(l.Name, l.Kind, LocalDetail(l, ctx), 0));
        foreach (var type in ctx.Types)
            foreach (var d in Index.MembersOf(type))
                result.Add(new Suggestion(d.Name, d.Kind, d.Detail, 1));
        foreach (var d in Index.All.Where(d => d.Container is null))
            result.Add(new Suggestion(d.Name, d.Kind, d.Detail, 2));
        foreach (var p in TypeTable.PrimitiveTypes) result.Add(new Suggestion(p.Name, "type", "builtin type", 3));

        return result.DistinctBy(s => (s.Label, s.Kind, s.Detail)).ToList();
    }

    static List<string> DeclarationKeywords(Place place, List<string> mods)
    {
        var list = new List<string>();
        var hasAccess = mods.Any(m => AccessMods.Contains(m));

        if (place is Place.Property)
        {
            list.AddRange(["get", "set", "init"]);
            if (!hasAccess) list.AddRange(AccessMods);
            return list;
        }

        if (!hasAccess) list.AddRange(AccessMods);
        list.AddRange(MemberMods.Where(m => !mods.Contains(m)));
        if (!mods.Any(m => InheritMods.Contains(m))) list.AddRange(InheritMods);
        list.AddRange(DeclKeywords);
        if (place is Place.Top && mods.Count == 0) list.AddRange(["import", "from", "module"]);
        return list;
    }

    // Modifiers directly in front of the cursor, e.g. `public static |`
    static List<string> ModifierRun(List<Token> sig, int from)
    {
        var mods = new List<string>();
        for (var k = from; k >= 0 && sig[k].Type is TokenType.AccessModifierKind or TokenType.MemberModifierKind or TokenType.InheritanceModifierKind; k--)
            mods.Add(sig[k].Value);
        return mods;
    }

    static bool IsExtensionKeyword(Token t) => t.Type is TokenType.InstanceKind && t.Value == "extension";

    static bool IntroducesName(List<Token> sig, int i)
    {
        var t = sig[i];
        if (t.Type is TokenType.VariableKind) return true;
        if (t.Type is not TokenType.InstanceKind) return false;
        if (t.Value == "fun") return !(i > 0 && IsExtensionKeyword(sig[i - 1]));
        return t.Value is "class" or "struct" or "record" or "trait" or "enum" or "annotation";
    }

    static bool TakesType(List<Token> sig, int i)
    {
        var t = sig[i];
        switch (t.Type)
        {
            case TokenType.Colon or TokenType.ArrowSymbol or TokenType.Is or TokenType.RefKeyword:
                return true;
            case TokenType.Not:
                return i > 0 && sig[i - 1].Type is TokenType.Is;
            case TokenType.InstanceKind:
                return t.Value is "extensions" or "constructor" or "destructor"
                       || (t.Value == "fun" && i > 0 && IsExtensionKeyword(sig[i - 1]));
            default:
                return false;
        }
    }

    List<Suggestion> TypeSuggestions(Ctx ctx)
    {
        var r = new List<Suggestion>();
        foreach (var g in ctx.Generics.Distinct()) r.Add(new Suggestion(g, "type parameter", $"type parameter {g}", 0));
        foreach (var d in Index.TypeDecls.Values) r.Add(new Suggestion(d.Name, d.Kind, d.Detail, 1));
        foreach (var p in TypeTable.PrimitiveTypes) r.Add(new Suggestion(p.Name, "type", "builtin type", 2));
        return r;
    }

    List<Suggestion> ImportSuggestions(List<Token> sig, int i)
    {
        var line = sig[i].Span.Start.Line;
        var isFromImport = false;
        for (var k = i - 1; k >= 0 && sig[k].Span.Start.Line == line; k--)
            if (sig[k].Type is TokenType.FromKeyword) isFromImport = true;

        if (sig[i].Type is TokenType.ImportKeyword && isFromImport)
            return Index.All.Where(d => d.Container is null)
                .Select(d => new Suggestion(d.Name, d.Kind, d.Detail, 1))
                .DistinctBy(s => s.Label).ToList();

        return snap.Table.Modules.Keys.Where(k => k.Length > 0)
            .Select(k => new Suggestion(k, "module", "module", 1)).ToList();
    }

    // Walks the braces before the cursor to learn what kind of body it is in
    Place PlaceAt(List<Token> sig, int limit)
    {
        var stack = new List<Place>();
        for (int i = 0; i < limit; i++)
        {
            if (sig[i].Type is TokenType.BracketOpen) stack.Add(BodyKind(sig, i, stack.Count == 0 ? Place.Top : stack[^1]));
            else if (sig[i].Type is TokenType.BracketClose && stack.Count > 0) stack.RemoveAt(stack.Count - 1);
        }

        return stack.Count == 0 ? Place.Top : stack[^1];
    }

    static Place BodyKind(List<Token> sig, int open, Place parent)
    {
        if (open == 0) return Place.Statement;

        // Collect the header in front of the brace; it ends at the previous brace or semicolon
        var header = new List<Token>();
        int depth = 0, line = sig[open - 1].Span.Start.Line;
        for (var k = open - 1; k >= 0; k--)
        {
            var t = sig[k];
            if (t.Type is TokenType.BracketOpen or TokenType.BracketClose or TokenType.Semicolon) break;
            if (t.Span.Start.Line != line && depth <= 0) break;

            if (t.Type is TokenType.ParenthesisClose) depth++;
            else if (t.Type is TokenType.ParenthesisOpen) depth--;

            line = t.Span.Start.Line;
            header.Add(t);
        }
        header.Reverse();

        foreach (var t in header.AsEnumerable().Reverse())
        {
            if (t.Type is TokenType.ModuleKeyword) return Place.Top;
            if (t.Type is not TokenType.InstanceKind) continue;

            return t.Value switch
            {
                "class" or "struct" or "record" or "trait" or "annotation" or "extensions" => Place.TypeBody,
                "enum" => Place.Enum,
                _ => Place.Statement
            };
        }

        // `Name: Type {` inside a type is a property
        var first = header.FindIndex(t => t.Type is not (TokenType.AccessModifierKind or TokenType.MemberModifierKind or TokenType.InheritanceModifierKind));
        if (parent is Place.TypeBody && first >= 0 && first + 1 < header.Count
            && header[first].Type is TokenType.Identifier && header[first + 1].Type is TokenType.Colon)
            return Place.Property;

        return Place.Statement;
    }

    List<Suggestion> Members(List<Token> sig, int recv, Ctx ctx)
    {
        var result = new List<Suggestion>();
        var owner = TypeNameAt(sig, recv, ctx, 0);
        if (owner is null) return result;

        var isStaticRef = recv >= 0 && sig[recv].Type is TokenType.Identifier
                          && !ctx.Locals.Any(l => l.Name == sig[recv].Value)
                          && Index.TypeNames.Contains(sig[recv].Value);

        foreach (var d in Index.MembersOf(owner))
            if (isStaticRef ? d.IsStatic : !d.IsStatic) result.Add(new Suggestion(d.Name, d.Kind, d.Detail, 1));
        return result.DistinctBy(s => (s.Label, s.Detail)).ToList();
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