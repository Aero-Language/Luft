using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.TypeChecker;

public class BodyResolver : AeroThrower
{
    private static readonly HashSet<AeroType> NumericTypes = [AeroType.Int, AeroType.Float, AeroType.Byte];

    private TypeTable Table { get; set; } = null!;

    // Ambient context for the 'self' and 'it' literals — set while descending into whichever
    // body currently defines them, restored on the way back out. Null means "not available here".
    private AeroType? CurrentSelfType { get; set; }
    private AeroType? CurrentItType { get; set; }

    public void Run(TypeTable typeTable)
    {
        Table = typeTable;

        foreach (var module in Table.Modules.Values)
        {
            CheckScope(module.Scope);
        }
    }
    
    private static BodyScope ScopeFor(ValueList<ParamNode> parameters)
    {
        var body = new BodyScope();
        foreach (var p in parameters) body.TryAdd(new VariableSymbol(p));
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
        foreach (var operators in scope.Operators.Values) CheckOperators(operators, scope);

        // Nested types weren't being descended into at all before, so their members were never
        // body-checked. Each type's own members see 'self' bound to that type while we're in here.
        foreach (var types in scope.Types.Values)
        {
            foreach (var type in types)
            {
                var previousSelf = CurrentSelfType;
                CurrentSelfType = SelfTypeFor(type);

                CheckScope(type.Scope);

                CurrentSelfType = previousSelf;
            }
        }
    }

    // 'self' inside a generic type's own body isn't the bare type — 'Box' and 'Box<T>' are
    // different types (a bare 'Box' wouldn't even resolve to anything, since every reference to
    // a generic type needs its argument list). Inside `class Box<T> { ... }`, 'self' refers to
    // this type applied to its own declared parameters, i.e. 'Box<T>', reusing the very same
    // GenericParameterType entries the type declared — so 'T' inside self's type is the same 'T'
    // already in scope, not a fresh/unrelated one.
    private static AeroType SelfTypeFor(TypeSymbol type)
    {
        var bare = new ScalarType(type.Name);
        return type.GenericParameters.Count == 0
            ? bare
            : new GenericType(bare, type.GenericParameters);
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
            var previousSelf = CurrentSelfType;
            CurrentSelfType = property.ExtensionTarget;

            if (property.Getter?.Body is not null)
                CheckStatements(property.Getter.Body.Statements, scope);
            
            if (property.Setter?.Body is not null)
                CheckStatements(property.Setter.Body.Statements, scope);
            
            if (property.Initializer is not null)
            {
                CheckTypes(ResolveExpression(property.Initializer, scope, new()), property.Type, scope, property.Span);
            }

            CurrentSelfType = previousSelf;
        }
    }
    private void CheckFunctions(List<FunctionSymbol> functions, TypeScope scope)
    {
        foreach (var function in functions)
        {
            if (function.Body is not null)
            {
                CheckStatements(function.Body.Statements, scope, ScopeFor(function.Declaration.Parameters), expectedReturn: function.ReturnType);
            }
        }
    }
    private void CheckExtensionFunctions(List<FunctionSymbol> functions, TypeScope scope)
    {
        foreach (var function in functions)
        {
            if (function.Body is not null)
            {
                var previousSelf = CurrentSelfType;
                CurrentSelfType = function.ExtensionTarget;

                CheckStatements(function.Body.Statements, scope, ScopeFor(function.Declaration.Parameters), expectedReturn: function.ReturnType);

                CurrentSelfType = previousSelf;
            }
        }
    }
    private void CheckOperators(List<OperatorSymbol> operators, TypeScope scope)
    {
        foreach (var op in operators)
        {
            if (op.Body is not null)
            {
                CheckStatements(op.Body.Statements, scope, ScopeFor(op.Declaration.Parameters), expectedReturn: op.ReturnType);
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
                    ResolveAssignment(a, scope, bScope);
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

    // Maps each compound-assignment operator to the plain binary operator it stands in for.
    // 'Assign' itself has no entry — it isn't a stand-in for anything, it's the base case.
    private static readonly Dictionary<Operator, Operator> CompoundToPlainOperator = new()
    {
        [Operator.AddAssign] = Operator.Add,
        [Operator.SubtractAssign] = Operator.Subtract,
        [Operator.MultiplyAssign] = Operator.Multiply,
        [Operator.DivideAssign] = Operator.Divide,
        [Operator.ModuloAssign] = Operator.Modulo,
        [Operator.AndAssign] = Operator.BitwiseAnd,
        [Operator.OrAssign] = Operator.BitwiseOr,
        [Operator.XorAssign] = Operator.BitwiseXor,
        [Operator.LeftShiftAssign] = Operator.LeftShift,
        [Operator.RightShiftAssign] = Operator.RightShift,
    };

    private void ResolveAssignment(AssignmentStatementNode a, TypeScope scope, BodyScope bScope)
    {
        var targetType = ResolveExpression(a.Target, scope, bScope);
        var valueType = ResolveExpression(a.Value, scope, bScope);

        if (a.Operator is Operator.Assign)
        {
            CheckTypes(valueType, targetType, scope, a.Span);
            return;
        }

        if (!CompoundToPlainOperator.TryGetValue(a.Operator, out var plainOp))
        {
            Error($"'{a.Operator.AsString()}' is not a valid assignment operator", a.Span);
            return;
        }

        // 'target op= value' is resolved exactly as 'target = target op value' would be:
        // run the plain operator through the same overload/primitive search a normal binary
        // expression uses, then check that its result still fits back into the target.
        var resultType = ResolveOperator(plainOp, targetType, valueType, scope, a.Span);
        CheckTypes(resultType, targetType, scope, a.Span);
    }

    // Small helper: report an error at `span` and hand back AeroType.Error so callers
    // can keep returning a value from a ternary/switch arm instead of a multi-line if-block.
    private AeroType Fail(string message, SourceSpan span)
    {
        Error(message, span);
        return AeroType.Error;
    }

    private AeroType ResolveExpression(ExpressionNode expression, TypeScope scope, BodyScope bScope,
        AeroType? expectedType = null)
    {
        var result = expression switch
        {
            BlockExpressionNode bl => ResolveBlock(bl, scope, bScope),
            IfExpressionNode ifExpr => ResolveIf(ifExpr, scope, bScope),
            ForExpressionNode f => ResolveFor(f, scope, bScope),
            MatchExpressionNode m => ResolveMatch(m, scope, bScope),
            LiteralExpressionNode l => ResolveLiteral(l),
            ArrayLiteralExpressionNode al => ResolveArrayLiteral(al, scope, bScope),
            IdentifierExpressionNode or MemberAccessExpressionNode
                => ToValue(ResolveName(expression, scope, bScope), expression.Span),
            CallExpressionNode c => ResolveCall(c, scope, bScope),
            BinaryExpressionNode b => ResolveBinary(b, scope, bScope),
            UnaryExpressionNode u => ResolveUnary(u, scope, bScope),
            ScopedExpressionNode sc => ResolveExpression(sc.Scoped, scope, bScope),
            IndexExpressionNode idx => ResolveIndex(idx, scope, bScope),
            RangeExpressionNode rg => ResolveRange(rg, scope, bScope),
            StringInterpolationExpressionNode interp => ResolveInterpolation(interp, scope, bScope),
            LambdaExpressionNode lam => ResolveLambda(lam, scope, bScope),
            ConcurrentExpressionNode con => ResolveBlock(con.Body, scope, bScope),
            SpawnExpressionNode sp => ResolveBlock(sp.Body, scope, bScope),
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
    private AeroType ResolveLiteral(LiteralExpressionNode expr)
    {
        return expr.LiteralType switch
        {
            TokenType.IntLiteral => AeroType.Int,
            TokenType.FloatLiteral => AeroType.Float,
            TokenType.CharLiteral => AeroType.Char,
            TokenType.StringLiteral => AeroType.String,
            TokenType.BooleanLiteral => AeroType.Bool,
            TokenType.NullLiteral => AeroType.Null,
            
            TokenType.ItLiteral => CurrentItType ?? Fail("The 'it' literal can not be used here", expr.Span),
            TokenType.SelfLiteral => CurrentSelfType ?? Fail("The 'self' literal can not be used here", expr.Span),
            
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

    // ---- Calls ----------------------------------------------------------

    private AeroType ResolveCall(CallExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        var target = ResolveName(expr.Target, scope, bScope);

        // Calling a stored lambda value (e.g. a field/variable typed as a lambda type)
        if (target is ValueRes { Type: LambdaType lambda })
        {
            var lambdaArgs = expr.Arguments.Select(a => ResolveExpression(a, scope, bScope)).ToArray();
            var lambdaParamTypes = lambda.Parameters.Select(p => p.Type).ToArray();

            if (!ArgsMatch(lambdaParamTypes, lambdaArgs))
                Error("Arguments do not match the lambda's parameter types", expr.Span);

            return lambda.ReturnType;
        }

        if (target is not FunctionsRes fn)
        {
            // Still resolve the arguments so further diagnostics inside them aren't lost
            foreach (var a in expr.Arguments) ResolveExpression(a, scope, bScope);

            // If the target itself already failed to resolve (e.g. an unknown module/identifier),
            // that error was reported at the source — piling "not callable" on top would just be
            // the same problem restated. Only genuinely-resolved-but-wrong things (a plain value,
            // a type, a module) earn a fresh "not callable" error.
            return target is ErrorRes ? AeroType.Error : Fail("Expression is not callable", expr.Span);
        }

        var argTypes = expr.Arguments.Select(a => ResolveExpression(a, scope, bScope)).ToArray();
        var matches = fn.Overloads.Where(o => ParametersMatch(o.Parameters, argTypes)).ToList();

        return matches.Count switch
        {
            0 => Fail($"No overload of '{fn.Overloads[0].Name}' matches the given arguments", expr.Span),
            > 1 => Fail($"Call to '{fn.Overloads[0].Name}' is ambiguous between {matches.Count} overloads", expr.Span),
            _ => matches[0].ReturnType
        };
    }

    // True if `argTypes` could be passed positionally to `parameters` — arity (accounting
    // for parameters that have a default initializer) plus per-position type equality.
    private static bool ParametersMatch(ValueList<ParamSymbol> parameters, AeroType[] argTypes)
    {
        if (argTypes.Length > parameters.Count) return false;

        var requiredCount = parameters.Count(p => p.Initializer is null);
        if (argTypes.Length < requiredCount) return false;

        for (int i = 0; i < argTypes.Length; i++)
        {
            if (argTypes[i] == AeroType.Error) continue; // already reported upstream, don't cascade
            if (argTypes[i] != parameters[i].Type) return false;
        }

        return true;
    }
    private static bool ArgsMatch(AeroType[] paramTypes, AeroType[] argTypes)
    {
        if (paramTypes.Length != argTypes.Length) return false;
        for (int i = 0; i < argTypes.Length; i++)
        {
            if (argTypes[i] == AeroType.Error) continue;
            if (argTypes[i] != paramTypes[i]) return false;
        }
        return true;
    }

    // ---- Binary / Unary ---------------------------------------------------

    private AeroType ResolveBinary(BinaryExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        // '::' is a cast, not a value-producing binary op: the left side names a type,
        // it isn't an expression to resolve the normal way.
        if (expr.Operator is Operator.CastSymbol) return ResolveCast(expr, scope, bScope);

        var left = ResolveExpression(expr.Left, scope, bScope);
        var right = ResolveExpression(expr.Right, scope, bScope);

        return ResolveOperator(expr.Operator, left, right, scope, expr.Span);
    }

    // Single entry point every binary op (and compound assignment) goes through: try a
    // user-defined overload first, fall back to the built-in primitive rules, and only
    // error if neither has anything for this (operator, left, right) combination.
    private AeroType ResolveOperator(Operator op, AeroType left, AeroType right, TypeScope scope, SourceSpan span)
    {
        if (left == AeroType.Error || right == AeroType.Error) return AeroType.Error;
        if (op is Operator.Is or Operator.Not) return AeroType.Bool; // pattern check, not a real operator

        if (TryOperatorOverload(op, left, [right], scope, span.FilePath) is { } overloadResult) return overloadResult;
        if (TryPrimitiveOperator(op, left, right) is { } primitiveResult) return primitiveResult;

        return Fail($"Operator '{op.AsString()}' is not defined for '{left}' and '{right}'", span);
    }

    // Looks up a real `operator fun` declaration (an OperatorDeclarationNode, registered by
    // TypeLookup as an OperatorSymbol under TypeScope.Operators) on `receiver`'s type — walking
    // base types the same way FindMember does. `argTypes` is the full argument list: one entry
    // for a binary operator's right-hand operand, zero for a unary operator. Returns null — not
    // an error — when there's no user type or no matching overload, so callers can fall through
    // to the primitive table.
    private AeroType? TryOperatorOverload(Operator op, AeroType receiver, AeroType[] argTypes, TypeScope scope, string filePath)
    {
        var symbol = FindType(receiver, scope, filePath);
        if (symbol is null) return null;

        return FindOperatorOverload(symbol, op, argTypes, [])?.ReturnType;
    }

    private OperatorSymbol? FindOperatorOverload(TypeSymbol type, Operator op, AeroType[] argTypes, HashSet<TypeSymbol> seen)
    {
        if (!seen.Add(type)) return null; // inheritance cycle guard, same idea as FindMember

        if (type.Scope.Operators.TryGetValue(op, out var overloads))
        {
            var match = overloads.FirstOrDefault(o => ParametersMatch(o.Parameters, argTypes));
            if (match is not null) return match;
        }

        foreach (var b in BasesOf(type))
            if (FindOperatorOverload(b, op, argTypes, seen) is { } inherited) return inherited;

        return null;
    }

    private static readonly HashSet<AeroType> IntegralTypes = [AeroType.Int, AeroType.Byte];

    // Built-in rules for the primitive types. Only 'Divide' gets special treatment: Int / Int
    // is true division and widens to Float (10 / 3 is 3.333..., not 3) — every other arithmetic
    // op keeps the operands' shared type, widening only when a Float is already involved.
    private AeroType? TryPrimitiveOperator(Operator op, AeroType left, AeroType right) => op switch
    {
        Operator.LogicalAnd or Operator.LogicalOr or Operator.And or Operator.Or
            => left == AeroType.Bool && right == AeroType.Bool ? AeroType.Bool : null,

        Operator.Equality or Operator.Inequality
            => left == right ? AeroType.Bool : null,

        Operator.LessThan or Operator.GreaterThan or Operator.LessThanEqual or Operator.GreaterThanEqual
            => Promote(left, right) is not null ? AeroType.Bool : null,

        Operator.BitwiseAnd or Operator.BitwiseOr or Operator.BitwiseXor
            or Operator.LeftShift or Operator.RightShift
            => IntegralTypes.Contains(left) && left == right ? left : null,

        Operator.Divide when left == AeroType.Int && right == AeroType.Int
            => AeroType.Float,

        Operator.Add or Operator.Subtract or Operator.Multiply or Operator.Modulo or Operator.Divide
            => Promote(left, right),

        _ => null
    };

    // Numeric promotion for primitives: identical types pass through unchanged; mixing an Int
    // or Byte with a Float widens to Float. Int/Byte mixed with each other has no defined
    // widening yet (comes back null, i.e. "no primitive rule matches").
    private static AeroType? Promote(AeroType left, AeroType right)
    {
        if (!NumericTypes.Contains(left) || !NumericTypes.Contains(right)) return null;
        if (left == right) return left;
        if (left == AeroType.Float || right == AeroType.Float) return AeroType.Float;
        return null;
    }

    private AeroType ResolveCast(BinaryExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        ResolveExpression(expr.Right, scope, bScope); // visited only for its own inner diagnostics

        // The parser builds a cast as a BinaryExpressionNode whose "Left" is actually a
        // prefix expression naming the target type (e.g. `Int` in `Int::x`), not a value.
        if (expr.Left is IdentifierExpressionNode targetName) return targetName.Name.ToType();

        return Fail("Expected a type name before '::'", expr.Span);
    }
    private AeroType ResolveUnary(UnaryExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        var operand = ResolveExpression(expr.Operand, scope, bScope);
        if (operand == AeroType.Error) return AeroType.Error;

        // A unary overload is an operator declaration with zero parameters (e.g. `operator fun Subtract() -> Vector2`
        // for unary '-'), as opposed to the one-parameter form binary '-' looks for.
        if (TryOperatorOverload(expr.Operator, operand, [], scope, expr.Span.FilePath) is { } overloadResult)
            return overloadResult;

        return expr.Operator switch
        {
            Operator.Increment or Operator.Decrement or Operator.Subtract or Operator.Add
                => NumericTypes.Contains(operand) ? operand : Fail($"'{expr.Operator.AsString()}' requires a numeric operand", expr.Span),
            Operator.LogicalNot
                => operand == AeroType.Bool ? AeroType.Bool : Fail("'!' requires a 'Bool' operand", expr.Span),
            Operator.Tilde
                => operand == AeroType.Int || operand == AeroType.Byte ? operand : Fail("'~' requires an integral operand", expr.Span),
            _ => operand
        };
    }

    // ---- Indexing / Ranges / Interpolation / Lambdas ----------------------

    private AeroType ResolveIndex(IndexExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        var target = ResolveExpression(expr.Target, scope, bScope);
        var index = ResolveExpression(expr.Index, scope, bScope);

        if (target == AeroType.Error) return AeroType.Error;
        if (target is not ArrayType array) return Fail($"Cannot index into '{target}'", expr.Span);
        if (index != AeroType.Error && index != AeroType.Int)
            Error($"Array index must be 'Int', got '{index}'", expr.Span);

        return array.ElementType;
    }
    private AeroType ResolveRange(RangeExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        if (expr.Left is not null) CheckTypes(ResolveExpression(expr.Left, scope, bScope), AeroType.Int, scope, expr.Span);
        if (expr.Right is not null) CheckTypes(ResolveExpression(expr.Right, scope, bScope), AeroType.Int, scope, expr.Span);
        return AeroType.Range;
    }
    private AeroType ResolveInterpolation(StringInterpolationExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        // Every embedded expression is resolved purely so errors inside it still surface —
        // interpolation itself accepts any type on the assumption it gets stringified.
        foreach (var part in expr.Parts) ResolveExpression(part, scope, bScope);
        return AeroType.String;
    }
    private LambdaType ResolveLambda(LambdaExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        var lambdaScope = new BodyScope(bScope);
        foreach (var p in expr.Parameters)
        {
            if (!lambdaScope.TryAdd(new VariableSymbol(p)))
                Error("Parameter already defined", p.Span);
        }

        var previousIt = CurrentItType;
        // 'it' is the implicit name for a single unnamed parameter: `{ System.Log(it) }`. The
        // moment a parameter is actually named (`{ x: Int -> ... }`), that name takes over and
        // 'it' is cancelled — hence the count check rather than "count == 1".
        // Note: this doesn't yet infer 'it's real type from the call site's expected lambda
        // signature (that needs the call/argument being matched, which ResolveLambda doesn't see) —
        // it stands in as Auto for now, so 'it' is usable but not yet type-checked against its use.
        CurrentItType = expr.Parameters.Count == 0 ? AeroType.Auto : null;

        var returnType = ResolveBlock(expr.Body, scope, lambdaScope);

        CurrentItType = previousIt;

        var paramTypes = expr.Parameters.Select(p => new TypeParam(p.Name, p.Type)).ToValueList();

        return new LambdaType(paramTypes, returnType);
    }

    private Resolved ResolveIdentifier(IdentifierExpressionNode expr, TypeScope scope, BodyScope bScope)
    {
        var name = expr.Name;
        var file = expr.Span.FilePath;
        
        if (bScope.Get(name) is { } local) return new ValueRes(local.Type);
        
        for (var s = scope; s is not null; s = s.ContainingScope)
            if (LookupInScope(s, name) is { } found) return found;
        
        foreach (var module in ImportedModules(file, name))
            if (LookupInScope(module.Scope, name) is { } imported) return imported;
        
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