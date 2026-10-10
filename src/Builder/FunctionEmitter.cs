using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

public sealed record DeclaredFunction(LLVMValueRef Value, LLVMTypeRef Type, TypeScope Scope);

// Lowers one function body. Statements live here, expressions in FunctionEmitter.Expressions.cs.
public sealed partial class FunctionEmitter : AeroThrower, IDisposable
{
    private readonly record struct LocalSlot(LLVMValueRef Address, LLVMTypeRef Type);

    protected override CompilerStage Stage => CompilerStage.CodeGen;

    private readonly LLVMContextRef _context;
    private readonly LLVMModuleRef _module;
    private readonly LLVMBuilderRef _builder;
    private readonly TypeLowering _types;
    private readonly TypedInfo _typed;
    private readonly IReadOnlyDictionary<FunctionSymbol, DeclaredFunction> _functions;
    private readonly Dictionary<string, LLVMValueRef> _strings;

    private readonly Dictionary<VariableSymbol, LocalSlot> _locals = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<(LLVMBasicBlockRef Break, LLVMBasicBlockRef Continue)> _loops = new();

    private LLVMValueRef _function;
    private LLVMBasicBlockRef _entry;
    private TypeScope _scope = null!;
    private AeroType _returnType = AeroType.Void;

    public bool HasErrors { get; private set; }

    public FunctionEmitter(LLVMContextRef context, LLVMModuleRef module, TypeLowering types, TypedInfo typed,
        IReadOnlyDictionary<FunctionSymbol, DeclaredFunction> functions, Dictionary<string, LLVMValueRef> strings)
    {
        _context = context;
        _module = module;
        _types = types;
        _typed = typed;
        _functions = functions;
        _strings = strings;
        _builder = context.CreateBuilder();
    }

    private void Fail(string message, SourceSpan span)
    {
        HasErrors = true;
        Error(message, span);
    }

    public void Emit(FunctionSymbol symbol, DeclaredFunction declared)
    {
        if (symbol.Body is null) return;

        _function = declared.Value;
        _scope = declared.Scope;
        _returnType = symbol.ReturnType;

        _entry = _context.AppendBasicBlock(_function, "entry");
        _builder.PositionAtEnd(_entry);

        var parameters = symbol.Declaration.Parameters;
        for (var i = 0; i < parameters.Count; i++) BindParameter(parameters[i], _function.GetParam((uint)i));

        LowerStatements(symbol.Body.Statements, true);

        if (IsTerminated()) return;
        if (_returnType == AeroType.Void) _builder.BuildRetVoid();
        else _builder.BuildUnreachable(); // the checker doesn't prove every path returns yet
    }

    private void BindParameter(ParamNode p, LLVMValueRef argument)
    {
        if (_typed.BindingOf(p) is not LocalBinding { Variable: var variable })
        {
            Fail($"The parameter '{p.Name}' was not resolved", p.Span);
            return;
        }
        if (!_types.TryLower(p.Type with { IsRef = false }, _scope, p.Span, out var valueType)) return;

        argument.Name = p.Name;

        if (p.Type.IsRef)
        {
            _locals[variable] = new LocalSlot(argument, valueType); // the argument already is the caller's address
            return;
        }

        var slot = EntryAlloca(valueType, p.Name);
        _builder.BuildStore(argument, slot);
        _locals[variable] = new LocalSlot(slot, valueType);
    }

    private LLVMBasicBlockRef AppendBlock(string name) => _context.AppendBasicBlock(_function, name);

    private bool IsTerminated() => _builder.InsertBlock.Terminator.Handle != IntPtr.Zero;

    private void BranchIfOpen(LLVMBasicBlockRef target)
    {
        if (!IsTerminated()) _builder.BuildBr(target);
    }

    // Allocas go to the top of the entry block so they stay promotable to registers
    private LLVMValueRef EntryAlloca(LLVMTypeRef type, string name)
    {
        using var b = _context.CreateBuilder();
        var first = _entry.FirstInstruction;
        if (first.Handle != IntPtr.Zero) b.PositionBefore(first);
        else b.PositionAtEnd(_entry);
        return b.BuildAlloca(type, name);
    }

    private void LowerStatements(ValueList<StatementNode> statements, bool isBody = false)
    {
        for (var i = 0; i < statements.Count; i++)
        {
            // Code after return/break/continue is unreachable but still has to live in a block
            if (IsTerminated()) _builder.PositionAtEnd(AppendBlock("dead"));
            LowerStatement(statements[i], isBody && i == statements.Count - 1);
        }
    }

    private void LowerStatement(StatementNode statement, bool isTail)
    {
        switch (statement)
        {
            case VariableStatementNode v: LowerVariable(v); break;
            case ReturnStatementNode r: LowerReturn(r); break;
            case WhileStatementNode w: LowerWhile(w); break;
            case AssignmentStatementNode a: LowerAssignment(a); break;
            case ExpressionStatementNode e: LowerExpressionStatement(e, isTail); break;
            case BreakStatementNode b: LowerJump(b.Span, loop => loop.Break); break;
            case ContinueStatementNode c: LowerJump(c.Span, loop => loop.Continue); break;
            case EmptyStatementNode or ImportStatementNode: break;
            default: Fail($"'{statement.GetType().Name}' is not lowered yet", statement.Span); break;
        }
    }

    private void LowerVariable(VariableStatementNode v)
    {
        if (_typed.BindingOf(v) is not LocalBinding { Variable: var variable })
        {
            Fail($"The variable '{v.Name}' was not resolved", v.Span);
            return;
        }

        var type = variable.Type;
        if (!_types.TryLower(type, _scope, v.Span, out var lowered)) return;
        if (lowered.Kind == LLVMTypeKind.LLVMVoidTypeKind)
        {
            Fail("A variable cannot have the type 'Void'", v.Span);
            return;
        }

        var slot = EntryAlloca(lowered, v.Name);
        _locals[variable] = new LocalSlot(slot, lowered);

        if (v.Initializer is null)
        {
            _builder.BuildStore(LLVMValueRef.CreateConstNull(lowered), slot); // zero initialized
            return;
        }

        if (!TryLowerValue(v.Initializer, out var init)) return;
        _builder.BuildStore(Coerce(init, _typed.TypeOf(v.Initializer), type, v.Span), slot);
    }

    private void LowerAssignment(AssignmentStatementNode a)
    {
        if (a.Operator != Operator.Assign)
        {
            LowerCompoundAssignment(a);
            return;
        }

        if (_typed.BindingOf(a.Target) is not LocalBinding { Variable: var variable } || !_locals.TryGetValue(variable, out var slot))
        {
            Fail("Only locals and parameters can be assigned so far", a.Target.Span);
            return;
        }

        if (!TryLowerValue(a.Value, out var value)) return;
        _builder.BuildStore(Coerce(value, _typed.TypeOf(a.Value), variable.Type, a.Span), slot.Address);
    }

    private void LowerReturn(ReturnStatementNode r)
    {
        if (r.Value is null)
        {
            _builder.BuildRetVoid();
            return;
        }

        if (_returnType == AeroType.Void)
        {
            TryLowerExpression(r.Value, out _); // evaluated for its effects only
            _builder.BuildRetVoid();
            return;
        }

        if (TryLowerValue(r.Value, out var value))
            _builder.BuildRet(Coerce(value, _typed.TypeOf(r.Value), _returnType, r.Span));
    }

    private void LowerJump(SourceSpan span, Func<(LLVMBasicBlockRef Break, LLVMBasicBlockRef Continue), LLVMBasicBlockRef> target)
    {
        if (_loops.Count == 0)
        {
            Fail("'break' and 'continue' can only be used in a loop", span);
            return;
        }
        _builder.BuildBr(target(_loops.Peek()));
    }

    private void LowerWhile(WhileStatementNode w)
    {
        var condition = AppendBlock("while.cond");
        var body = AppendBlock("while.body");
        var end = AppendBlock("while.end");

        _builder.BuildBr(condition);

        _builder.PositionAtEnd(condition);
        if (!TryLowerCondition(w.Condition, out var test)) return;
        _builder.BuildCondBr(test, body, end);

        _builder.PositionAtEnd(body);
        _loops.Push((end, condition));
        LowerStatements(w.Body.Statements);
        _loops.Pop();
        BranchIfOpen(condition);

        _builder.PositionAtEnd(end);
    }

    private void LowerExpressionStatement(ExpressionStatementNode statement, bool isTail)
    {
        var returnsValue = isTail && _returnType != AeroType.Void;

        switch (statement.Expression)
        {
            case IfExpressionNode ifExpr when !returnsValue:
                LowerIf(ifExpr);
                return;
            case BlockExpressionNode block when !returnsValue:
                LowerStatements(block.Statements);
                return;
        }

        // The last expression of a body is its result
        if (returnsValue)
        {
            if (TryLowerValue(statement.Expression, out var value))
                _builder.BuildRet(Coerce(value, _typed.TypeOf(statement.Expression), _returnType, statement.Span));
            return;
        }

        TryLowerExpression(statement.Expression, out _);
    }

    // Statement form only, an if that produces a value is lowered in sub-task 5
    private void LowerIf(IfExpressionNode e)
    {
        var branches = new List<(ExpressionNode Condition, BlockExpressionNode Body)> { (e.Condition, e.ThenBody) };
        branches.AddRange(e.ElseIfs.Select(x => (x.condition, x.body)));

        var end = AppendBlock("if.end");

        for (var i = 0; i < branches.Count; i++)
        {
            var then = AppendBlock("if.then");
            var last = i == branches.Count - 1;
            var next = last && e.ElseBody is null ? end : AppendBlock("if.else");

            if (!TryLowerCondition(branches[i].Condition, out var test)) return;
            _builder.BuildCondBr(test, then, next);

            _builder.PositionAtEnd(then);
            LowerStatements(branches[i].Body.Statements);
            BranchIfOpen(end);

            _builder.PositionAtEnd(next);
        }

        if (e.ElseBody is not null)
        {
            LowerStatements(e.ElseBody.Statements);
            BranchIfOpen(end);
        }

        _builder.PositionAtEnd(end);
    }

    private bool TryLowerCondition(ExpressionNode expression, out LLVMValueRef test)
    {
        test = default;
        if (!TryLowerValue(expression, out var value)) return false;

        // Bool is stored as i8, branches need an i1
        test = _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, value, LLVMValueRef.CreateConstInt(_context.Int8Type, 0), "cond");
        return true;
    }

    public void Dispose() => _builder.Dispose();
}