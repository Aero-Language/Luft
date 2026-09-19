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
                CheckStatements(function.Body.Statements, scope, expectedReturn: function.ReturnType);
            }
        }
    }
    private void CheckExtensionFunctions(List<FunctionSymbol> functions, TypeScope scope)
    {
        foreach (var function in functions)
        {
            if (function.Body is not null)
            {
                CheckStatements(function.Body.Statements, scope, expectedReturn: function.ReturnType);
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
                    CheckTypes(ResolveExpression(r.Value, scope, bScope), expectedReturn, scope, r.Span);
                    break;
                case ContinueStatementNode or BreakStatementNode:
                    if (!isLoop) Error("Continue and break can only be used in a loop", statement.Span);
                    break;
                case WhileStatementNode w:
                    CheckTypes(ResolveExpression(w.Condition, scope, bScope), AeroType.Bool, scope, w.Span);
                    CheckStatements(w.Body.Statements, scope, isLoop: true);
                    break;
                case ExpressionStatementNode e:
                    bool wasChecked = false;
                    if (i == statements.Count - 1) // Last statement, can be auto-return
                    {
                        if (expectedReturn != AeroType.Void)
                        {
                            CheckTypes(expectedReturn, ResolveExpression(e.Expression, scope, bScope), scope, e.Span);
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
        if (t1 == AeroType.Error || t2 == AeroType.Error) return; // Ignore error types as they are placeholders
        if (t1 != t2) Error($"Cannot convert type '{t1}' to '{t1}'", span);
    }

    private AeroType ResolveIdentifier(string name, BodyScope bScope, TypeScope scope, SourceSpan location)
    {
        var local = bScope.Get(name);
        
        
    }
    
    private AeroType ResolveExpression(ExpressionNode expression, TypeScope scope, BodyScope bScope, AeroType? expectedType = null, AeroType? itType = null, AeroType? selfType = null)
    {
        return expression switch
        {
            BlockExpressionNode bl => ResolveBlock(bl, scope, bScope),
            IfExpressionNode i => ResolveIf(i, scope, bScope),
            ForExpressionNode f => ResolveFor(f, scope, bScope),
            MatchExpressionNode m => ResolveMatch(m, scope, bScope),
            LiteralExpressionNode l => ResolveLiteral(l, itType ?? AeroType.Error, selfType ?? AeroType.Error),
            _ => ResolveError(expression)
        };
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
        if (!bScope.TryAdd(new(expr.Item))) Error("Variable already declared in scope", expr.Span);
        var cType = ResolveExpression(expr.Collection, scope, bScope);
        CheckStatements(expr.Body.Statements, scope);
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
}