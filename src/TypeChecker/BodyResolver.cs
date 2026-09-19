using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.TypeChecker;

public class BodyResolver : AeroThrower
{
    private TypeTable Table { get; set; } = null!;

    public void Run(TypeTable typeTable)
    {
        Table = typeTable;

        foreach (var module in Table.Modules.Values)
        {
            CheckScope(module.Scope);
        }
    }
    
    private static BodyScope ScopeFor(FunctionSymbol function)
    {
        var body = new BodyScope();
        foreach (var p in function.Declaration.Parameters) body.TryAdd(new VariableSymbol(p));
        return body;
    }
    
    private void CheckScope(TypeScope scope)
    {
        foreach (var fields in scope.Fields.Values) CheckFields(fields, scope);
        foreach (var properties in scope.Properties.Values) CheckProperties(properties, scope);
        foreach (var extensionProperties in scope.ExtensionProperties.Values)
            CheckExtensionProperties(extensionProperties, scope);
        foreach (var functions in scope.Functions.Values) CheckFunctions(functions, scope);
        foreach (var extensionFunctions in scope.ExtensionFunctions.Values)
            CheckExtensionFunctions(extensionFunctions, scope);
    }
    private void CheckFields(List<FieldSymbol> fields, TypeScope scope)
    {
        foreach (var field in fields)
        {
            if (field.Initializer is not null)
            {
                CheckTypes(ResolveExpression(field.Initializer, scope, new()), field.Type, scope, field.Span);
            }
        }
    }
    private void CheckProperties(List<PropertySymbol> properties, TypeScope scope)
    {
        foreach (var property in properties)
        {
            if (property.Getter?.Body is not null)
                CheckStatements(property.Getter.Body.Statements, scope);
            
            if (property.Setter?.Body is not null)
                CheckStatements(property.Setter.Body.Statements, scope);
            
            if (property.Initializer is not null)
            {
                CheckTypes(ResolveExpression(property.Initializer, scope, new()), property.Type, scope, property.Span);
            }
        }
    }
    private void CheckExtensionProperties(List<PropertySymbol> properties, TypeScope scope)
    {
        foreach (var property in properties)
        {
            if (property.Getter?.Body is not null)
                CheckStatements(property.Getter.Body.Statements, scope);
            
            if (property.Setter?.Body is not null)
                CheckStatements(property.Setter.Body.Statements, scope);
            
            if (property.Initializer is not null)
            {
                CheckTypes(ResolveExpression(property.Initializer, scope, new()), property.Type, scope, property.Span);
            }
        }
    }
    private void CheckFunctions(List<FunctionSymbol> functions, TypeScope scope)
    {
        foreach (var function in functions)
        {
            if (function.Body is not null)
            {
                CheckStatements(function.Body.Statements, scope, ScopeFor(function), expectedReturn: function.ReturnType);
            }
        }
    }
    private void CheckExtensionFunctions(List<FunctionSymbol> functions, TypeScope scope)
    {
        foreach (var function in functions)
        {
            if (function.Body is not null)
            {
                CheckStatements(function.Body.Statements, scope, ScopeFor(function), expectedReturn: function.ReturnType);
            }
        }
    }

    private void CheckStatements(ValueList<StatementNode> statements, TypeScope scope, BodyScope? bScope = null,
        bool isLoop = false, AeroType? expectedReturn = null)
    {
        if (bScope is null) bScope = new();
        else bScope = new(bScope);

        for (int i = 0; i < statements.Count; i++)
        {
            var statement = statements[i];
            switch (statement)
            {
                case VariableStatementNode v:
                    if (v.Initializer is not null) ResolveExpression(v.Initializer, scope, bScope);
                    if (!bScope.TryAdd(new(v))) Error("Variable already defined", v.Span);
                    break;
                case ReturnStatementNode r:
                    var returned = r.Value is null ? AeroType.Void : ResolveExpression(r.Value, scope, bScope);
                    CheckTypes(returned, expectedReturn, scope, r.Span);
                    break;
                case ContinueStatementNode or BreakStatementNode:
                    if (!isLoop) Error("Continue and break can only be used in a loop", statement.Span);
                    break;
                case WhileStatementNode w:
                    CheckTypes(ResolveExpression(w.Condition, scope, bScope), AeroType.Bool, scope, w.Span);
                    CheckStatements(w.Body.Statements, scope, bScope, isLoop: true);   // pass bScope
                    break;
                case ExpressionStatementNode e:
                    bool wasChecked = false;
                    if (i == statements.Count - 1) // Last statement, can be auto-return
                    {
                        if (expectedReturn != AeroType.Void)
                        {
                            CheckTypes(ResolveExpression(e.Expression, scope, bScope), expectedReturn, scope, e.Span);
                            wasChecked = true;
                        }
                    }

                    if (!wasChecked) ResolveExpression(e.Expression, scope, bScope);
                    break;
                case AssignmentStatementNode a:
                    CheckTypes(ResolveExpression(a.Value, scope, bScope), ResolveExpression(a.Target, scope, bScope),
                        scope, a.Span);
                    break;
            }
        }
    }

    private void CheckTypes(AeroType? t1, AeroType? t2, TypeScope scope, SourceSpan span)
    {
        if (t1 is null || t2 is null) return;
        if (t1 == AeroType.Error || t2 == AeroType.Error) return;
        if (t1 != t2) Error($"Cannot convert type '{t1}' to '{t2}'", span);
    }
    
    private AeroType ResolveExpression(ExpressionNode expression, TypeScope scope, BodyScope bScope,
        AeroType? expectedType = null, AeroType? itType = null, AeroType? selfType = null)
    {
        var result = expression switch
        {
            BlockExpressionNode bl => ResolveBlock(bl, scope, bScope),
            IfExpressionNode ifExpr => ResolveIf(ifExpr, scope, bScope),
            ForExpressionNode f => ResolveFor(f, scope, bScope),
            MatchExpressionNode m => ResolveMatch(m, scope, bScope),
            LiteralExpressionNode l => ResolveLiteral(l, itType ?? AeroType.Error, selfType ?? AeroType.Error),
            ArrayLiteralExpressionNode al => ResolveArrayLiteral(al, scope, bScope),
            IdentifierExpressionNode or MemberAccessExpressionNode
                => ToValue(ResolveName(expression, scope, bScope), expression.Span),
            _ => ResolveError(expression)
        };

        if (expectedType is not null && result != AeroType.Error && result != expectedType)
        {
            Error($"Expected '{expectedType}' instead of '{result}'", expression.Span);
            return AeroType.Error;
        }

        return result;
    }

    private AeroType ResolveError(ExpressionNode expr)
    {
        Error("Expression type could not be resolved", expr.Span);
        return AeroType.Void;
    }
    private AeroType ResolveBlock(BlockExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        CheckStatements(expr.Statements, scope, bScope);

        if (expr.Statements[^1] is ReturnStatementNode r && r.Value is not null)
            return ResolveExpression(r.Value, scope, bScope);
        if (expr.Statements[^1] is ExpressionStatementNode e)
            return ResolveExpression(e.Expression, scope, bScope);
        return AeroType.Void;
    }
    private AeroType ResolveIf(IfExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        CheckTypes(ResolveExpression(expr.Condition, scope, bScope), AeroType.Bool, scope, expr.Span);
        var returnType = ResolveExpression(expr.ThenBody, scope, bScope);

        foreach (var elseIf in expr.ElseIfs)
        {
            CheckTypes(ResolveExpression(elseIf.condition, scope, bScope), AeroType.Bool, scope, expr.Span);
            CheckTypes(ResolveExpression(elseIf.body, scope, bScope), returnType, scope, elseIf.condition.Span);
        }

        CheckTypes(ResolveExpression(expr.ThenBody, scope, bScope), returnType, scope, expr.ThenBody.Span);
        return returnType;
    }
    private AeroType ResolveFor(ForExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        var cType = ResolveExpression(expr.Collection, scope, bScope);

        var loopScope = new BodyScope(bScope);           // loop variable lives only inside the loop
        if (!loopScope.TryAdd(new(expr.Item))) Error("Variable already declared in scope", expr.Span);

        CheckStatements(expr.Body.Statements, scope, loopScope, isLoop: true);
        return cType;
    }
    private AeroType ResolveMatch(MatchExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        ResolveExpression(expr.Target, scope, bScope);
        var expected = AeroType.Void;
            
        if (expr.Cases.Count != 0)
        {
            expected = ResolveExpression(expr.Cases.First(), scope, bScope);
                
            foreach (var caseNode in expr.Cases)
            {
                ResolveExpression(caseNode.Pattern, scope, bScope, expected);
                CheckStatements(caseNode.Body.Statements, scope, bScope);
            }
        }

        return expected;
    }
    private AeroType ResolveLiteral(LiteralExpressionNode expr, AeroType itType, AeroType selfType)
    {
        if (itType == AeroType.Error) Error("The 'it' literal can not be used here", expr.Span);
        if (selfType == AeroType.Error) Error("The 'self' literal can not be used here", expr.Span);
        
        return expr.LiteralType switch
        {
            TokenType.IntLiteral => AeroType.Int,
            TokenType.FloatLiteral => AeroType.Float,
            TokenType.CharLiteral => AeroType.Char,
            TokenType.StringLiteral => AeroType.String,
            TokenType.BooleanLiteral => AeroType.Bool,
            TokenType.NullLiteral => AeroType.Null,
            
            TokenType.ItLiteral => itType,
            TokenType.SelfLiteral => selfType,
            
            _ => AeroType.Void
        };
    }
    private ArrayType ResolveArrayLiteral(ArrayLiteralExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        if (!expr.Elements.Any()) return new ArrayType(AeroType.Auto);

        var result = ResolveExpression(expr.Elements[0], scope, bScope);
        foreach (var element in expr.Elements.Skip(1))
        {
            var t = ResolveExpression(element, scope, bScope);
            if (t != AeroType.Error && result != AeroType.Error && t != result)
                Error($"Expected '{result}' instead of '{t}'", element.Span);
        }

        return new ArrayType(result);
    }
    private Resolved ResolveIdentifier(IdentifierExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        var name = expr.Name;
        var file = expr.Span.FilePath;

        // 1. Locals
        if (bScope.Get(name) is { } local) return new ValueRes(local.Type);

        // 2. Members and types, walking outwards (class scope -> module scope)
        for (var s = scope; s is not null; s = s.ContainingScope)
            if (LookupInScope(s, name) is { } found) return found;

        // 3. Imported modules
        foreach (var module in ImportedModules(file, name))
            if (LookupInScope(module.Scope, name) is { } imported) return imported;

        // 4. Module names: 'System' after 'import Core.System', or the start of a path like 'Core'
        foreach (var import in ImportsOf(file))
            if (import.Imports.Count == 0 && import.TargetPath.Split('.')[^1] == name)
                return new ModuleRes(import.TargetPath);
        if (IsModulePath(name)) return new ModuleRes(name);

        Error($"'{name}' could not be found", expr.Span);
        return Failed;
    }
    private Resolved ResolveMemberAccess(MemberAccessExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        if (expr.Member is not IdentifierExpressionNode member)
        {
            Error("Expected a member name after '.'", expr.Span);
            return Failed;
        }

        var name = member.Name;
        var target = ResolveName(expr.Target, scope, bScope); // recurse on Target only

        switch (target)
        {
            case ModuleRes m:
            {
                if (Table.Modules.TryGetValue(m.Path, out var module)
                    && LookupInScope(module.Scope, name) is { } inModule)
                    return inModule;

                var deeper = $"{m.Path}.{name}";
                if (IsModulePath(deeper)) return new ModuleRes(deeper);

                Error($"Module '{m.Path}' has no member '{name}'", expr.Span);
                return Failed;
            }
            case TypeRes t:
                return Access(t.Symbol, name, wantStatic: true, expr.Span);

            case ValueRes v:
            {
                var symbol = FindType(v.Type, scope, expr.Span.FilePath);
                if (symbol is null)
                {
                    Error($"Cannot access members of type '{v.Type}'", expr.Span);
                    return Failed;
                }
                return Access(symbol, name, wantStatic: false, expr.Span);
            }
            case FunctionsRes:
                Error("Cannot access members of a function", expr.Span);
                return Failed;

            default: // ErrorRes: already reported
                return Failed;
        }
    }
    
    private TypeSymbol? FindType(AeroType? type, TypeScope scope, string filePath) => type switch
    {
        ScalarType s  => FindTypeByName(s.Name, 0, scope, filePath),
        GenericType g => FindTypeByName(g.Definition.Name, g.TypeArguments.Count, scope, filePath),
        _ => null // primitives, arrays, lambdas and special types have no TypeSymbol (yet)
    };

    private TypeSymbol? FindTypeByName(string name, int arity, TypeScope scope, string filePath)
    {
        for (var s = scope; s is not null; s = s.ContainingScope)
            if (s.Types.TryGetValue(name, out var local)
                && local.FirstOrDefault(t => t.GenericParameters.Count == arity) is { } hit)
                return hit;

        foreach (var module in ImportedModules(filePath, name))
            if (module.Scope.Types.TryGetValue(name, out var imported)
                && imported.FirstOrDefault(t => t.GenericParameters.Count == arity) is { } found)
                return found;

        return null;
    }

    private IEnumerable<ImportStatementNode> ImportsOf(string file)
        => Table.ImportsByFile.TryGetValue(file, out var list) ? list : Enumerable.Empty<ImportStatementNode>();

    private IEnumerable<ModuleSymbol> ImportedModules(string file, string name)
    {
        foreach (var import in ImportsOf(file))
        {
            // 'from X import A, B' only exposes A and B
            if (import.Imports.Count > 0 && !import.Imports.Contains(name)) continue;
            if (Table.Modules.TryGetValue(import.TargetPath, out var module)) yield return module;
        }
    }

    private bool IsModulePath(string path)
        => Table.Modules.Keys.Any(k => k == path || k.StartsWith(path + "."));
    
    
    private static Resolved? LookupInScope(TypeScope s, string name)
    {
        if (s.Fields.TryGetValue(name, out var fields))    return new ValueRes(fields[0].Type);
        if (s.Properties.TryGetValue(name, out var props)) return new ValueRes(props[0].Type);
        if (s.Functions.TryGetValue(name, out var funcs))  return new FunctionsRes(funcs);
        if (s.Types.TryGetValue(name, out var types))      return new TypeRes(types[0]);
        return null;
    }

    private IEnumerable<TypeSymbol> BasesOf(TypeSymbol symbol)
    {
        IEnumerable<AeroType> bases = symbol switch
        {
            ClassSymbol c  => c.Node.Implements,
            StructSymbol s => s.Node.Implements,
            RecordSymbol r => r.Node.Implements,
            TraitSymbol t  => t.Node.Traits,
            _ => []
        };

        foreach (var b in bases)
            if (FindType(b, symbol.Scope, symbol.Span.FilePath) is { } found) yield return found;
    }

    private static bool IsStatic(MemberMod mods, VariableKind? kind = null)
        => mods.HasFlag(MemberMod.Static) || kind == VariableKind.Const; // const is implicitly static (Player.MAX_HEALTH)

    private Member? FindMember(TypeSymbol type, string name, HashSet<TypeSymbol> seen)
    {
        if (!seen.Add(type)) return null; // inheritance cycle guard

        if (type is EnumSymbol e)
        {
            if (e.Members.Any(m => m.Name == name))
                return new Member(new ValueRes(new ScalarType(e.Name)), IsStatic: true);
            if (e.Parameters.FirstOrDefault(p => p.Name == name) is { } param)
                return new Member(new ValueRes(param.Type), IsStatic: false);
        }

        var s = type.Scope;
        if (s.Fields.TryGetValue(name, out var fields))
            return new Member(new ValueRes(fields[0].Type), IsStatic(fields[0].Declaration.MemberMods, fields[0].VarKind));
        if (s.Properties.TryGetValue(name, out var props))
            return new Member(new ValueRes(props[0].Type), IsStatic(props[0].Declaration.MemberMods));
        if (s.Functions.TryGetValue(name, out var funcs))
            return new Member(new FunctionsRes(funcs), funcs.All(f => f.MemberMods.HasFlag(MemberMod.Static)));
        if (s.Types.TryGetValue(name, out var nested))
            return new Member(new TypeRes(nested[0]), IsStatic: true);

        foreach (var b in BasesOf(type))
            if (FindMember(b, name, seen) is { } inherited) return inherited;

        return null;
    }
    
    
    
    private Resolved ResolveName(ExpressionNode expr, TypeScope scope, BodyScope bScope) => expr switch
    {
        IdentifierExpressionNode id   => ResolveIdentifier(id, scope, bScope),
        MemberAccessExpressionNode ma => ResolveMemberAccess(ma, scope, bScope),
        _ => new ValueRes(ResolveExpression(expr, scope, bScope))
    };
    private Resolved Access(TypeSymbol symbol, string name, bool wantStatic, SourceSpan span)
    {
        var member = FindMember(symbol, name, []);
        if (member is null)
        {
            Error($"'{symbol.Name}' has no member '{name}'", span);
            return Failed;
        }
        if (wantStatic && !member.IsStatic)
        {
            Error($"'{name}' is an instance member and needs an instance of '{symbol.Name}'", span);
            return Failed;
        }
        if (!wantStatic && member.IsStatic)
        {
            Error($"'{name}' is static, access it through '{symbol.Name}'", span);
            return Failed;
        }
        return member.Result;
    }

    // Used when an expression must be a VALUE (not a type, module or function group)
    private AeroType ToValue(Resolved resolved, SourceSpan span)
    {
        switch (resolved)
        {
            case ValueRes v: return v.Type;
            case FunctionsRes { Overloads: [var only] }:
                return new LambdaType(only.Parameters.Select(p => new TypeParam(p.Name, p.Type)).ToValueList(), only.ReturnType);
            case FunctionsRes: Error("An overloaded function cannot be used as a value", span); return AeroType.Error;
            case TypeRes t:    Error($"'{t.Symbol.Name}' is a type, not a value", span);         return AeroType.Error;
            case ModuleRes m:  Error($"'{m.Path}' is a module, not a value", span);              return AeroType.Error;
            default: return AeroType.Error; // ErrorRes
        }
    }
    
    
    
    
    private abstract record Resolved;
    private sealed record ValueRes(AeroType Type) : Resolved;
    private sealed record TypeRes(TypeSymbol Symbol) : Resolved;
    private sealed record ModuleRes(string Path) : Resolved;
    private sealed record FunctionsRes(List<FunctionSymbol> Overloads) : Resolved;
    private sealed record ErrorRes : Resolved;   // error already reported, stay silent
    private static readonly ErrorRes Failed = new();

    private sealed record Member(Resolved Result, bool IsStatic);
}