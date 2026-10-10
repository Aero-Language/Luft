using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.Utility;

namespace Luft.Builder;

// Short-circuit logic, if/match (statement or value), for over ranges, patterns and ranges
public sealed partial class FunctionEmitter
{
    // The match target inside a case, available as the literal 'it'
    private readonly Stack<LLVMValueRef> _itValues = new();

    private LLVMValueRef ToI1(LLVMValueRef value, string name)
        => _builder.BuildICmp(LLVMIntPredicate.LLVMIntNE, value, LLVMValueRef.CreateConstInt(_context.Int8Type, 0), name);

    private bool TryLowerShortCircuit(BinaryExpressionNode b, out LLVMValueRef value)
    {
        value = default;
        if (!TryLowerValue(b.Left, out var left)) return false;

        var isAnd = b.Operator is Operator.And or Operator.LogicalAnd;
        var leftBlock = _builder.InsertBlock;
        var rhs = AppendBlock(isAnd ? "and.rhs" : "or.rhs");
        var end = AppendBlock(isAnd ? "and.end" : "or.end");

        var test = ToI1(left, "lhs");
        if (isAnd) _builder.BuildCondBr(test, rhs, end);
        else _builder.BuildCondBr(test, end, rhs);

        _builder.PositionAtEnd(rhs);
        if (!TryLowerValue(b.Right, out var right)) return false;
        var rightBlock = _builder.InsertBlock;
        _builder.BuildBr(end);

        _builder.PositionAtEnd(end);
        var phi = _builder.BuildPhi(_context.Int8Type, isAnd ? "and" : "or");
        var shortValue = LLVMValueRef.CreateConstInt(_context.Int8Type, isAnd ? 0UL : 1UL);
        phi.AddIncoming(new[] { shortValue, right }, new[] { leftBlock, rightBlock }, 2);
        value = phi;
        return true;
    }

    // The last expression of a block is its value; a block that ends otherwise has none
    private bool TryLowerBlockValue(BlockExpressionNode block, out LLVMValueRef value)
    {
        value = default;
        var statements = block.Statements;

        for (var i = 0; i < statements.Count; i++)
        {
            if (IsTerminated()) _builder.PositionAtEnd(AppendBlock("dead"));

            if (i == statements.Count - 1 && statements[i] is ExpressionStatementNode e)
                return TryLowerExpression(e.Expression, out value);

            LowerStatement(statements[i], false);
        }
        return true;
    }

    // A result is only built when the type has one and every path is covered by an else
    private bool WantsValue(AeroType? type, bool hasElse)
        => hasElse && type is not null && type != AeroType.Void && type != AeroType.Error;

    private bool LowerBranch(BlockExpressionNode body, bool wantValue, AeroType resultType, LLVMBasicBlockRef end,
        List<(LLVMValueRef Value, LLVMBasicBlockRef Block)> incoming)
    {
        LLVMValueRef value = default;

        if (wantValue)
        {
            if (!TryLowerBlockValue(body, out value)) return false;
        }
        else
        {
            LowerStatements(body.Statements);
        }

        if (IsTerminated()) return true;

        if (wantValue)
        {
            if (value.Handle == IntPtr.Zero)
            {
                Fail("Every branch has to end in a value here", body.Span);
                return false;
            }

            incoming.Add((Coerce(value, _typed.TypeOf(body), resultType, body.Span), _builder.InsertBlock));
        }

        _builder.BuildBr(end);
        return true;
    }

    // Leaves the builder in the end block; value stays default for the statement form
    private bool FinishMerge(bool wantValue, LLVMTypeRef lowered, LLVMBasicBlockRef end,
        List<(LLVMValueRef Value, LLVMBasicBlockRef Block)> incoming, string name, out LLVMValueRef value)
    {
        value = default;
        _builder.PositionAtEnd(end);
        if (!wantValue) return true;

        if (incoming.Count == 0)
        {
            _builder.BuildUnreachable(); // every branch returned or jumped away
            return true;
        }

        var phi = _builder.BuildPhi(lowered, name);
        phi.AddIncoming(incoming.Select(x => x.Value).ToArray(), incoming.Select(x => x.Block).ToArray(), (uint)incoming.Count);
        value = phi;
        return true;
    }

    private bool TryLowerIf(IfExpressionNode e, out LLVMValueRef value)
    {
        value = default;

        var type = _typed.TypeOf(e) ?? AeroType.Void;
        var wantValue = WantsValue(type, e.ElseBody is not null);
        var lowered = default(LLVMTypeRef);
        if (wantValue && !_types.TryLower(type, _scope, e.Span, out lowered)) return false;

        var branches = new List<(ExpressionNode Condition, BlockExpressionNode Body)> { (e.Condition, e.ThenBody) };
        branches.AddRange(e.ElseIfs.Select(x => (x.condition, x.body)));

        var end = AppendBlock("if.end");
        var incoming = new List<(LLVMValueRef, LLVMBasicBlockRef)>();

        for (var i = 0; i < branches.Count; i++)
        {
            var then = AppendBlock("if.then");
            var last = i == branches.Count - 1;
            var next = last && e.ElseBody is null ? end : AppendBlock("if.else");

            if (!TryLowerCondition(branches[i].Condition, out var test)) return false;
            _builder.BuildCondBr(test, then, next);

            _builder.PositionAtEnd(then);
            if (!LowerBranch(branches[i].Body, wantValue, type, end, incoming)) return false;

            _builder.PositionAtEnd(next);
        }

        if (e.ElseBody is not null && !LowerBranch(e.ElseBody, wantValue, type, end, incoming)) return false;

        return FinishMerge(wantValue, lowered, end, incoming, "if.val", out value);
    }

    private bool TryLowerMatch(MatchExpressionNode m, out LLVMValueRef value)
    {
        value = default;

        if (!TryLowerValue(m.Target, out var target)) return false;
        var targetType = _typed.TypeOf(m.Target);
        if (targetType is null)
        {
            Fail("The match target was not resolved", m.Span);
            return false;
        }

        var type = _typed.TypeOf(m) ?? AeroType.Void;
        var wantValue = WantsValue(type, m.Cases.Count > 0 && m.Cases[^1].Pattern is ElsePattern);
        var lowered = default(LLVMTypeRef);
        if (wantValue && !_types.TryLower(type, _scope, m.Span, out lowered)) return false;

        var end = AppendBlock("match.end");
        var incoming = new List<(LLVMValueRef, LLVMBasicBlockRef)>();

        if (m.Cases.Count == 0) _builder.BuildBr(end);

        _itValues.Push(target);
        try
        {
            for (var i = 0; i < m.Cases.Count; i++)
            {
                var c = m.Cases[i];
                var last = i == m.Cases.Count - 1;
                var body = AppendBlock("case.body");

                // Falling off the last case needs an else to be a value, so that path can never be taken
                LLVMBasicBlockRef next;
                if (!last) next = AppendBlock("case.next");
                else if (wantValue) next = AppendBlock("match.fail");
                else next = end;

                if (c.Pattern is ElsePattern && c.Guard is null)
                {
                    _builder.BuildBr(body);
                }
                else
                {
                    if (!TryLowerPattern(c.Pattern, target, targetType, c.Span, out var matched)) return false;

                    if (c.Guard is null)
                    {
                        _builder.BuildCondBr(ToI1(matched, "case"), body, next);
                    }
                    else
                    {
                        var guard = AppendBlock("case.guard");
                        _builder.BuildCondBr(ToI1(matched, "case"), guard, next);

                        _builder.PositionAtEnd(guard);
                        if (!TryLowerCondition(c.Guard, out var guardTest)) return false;
                        _builder.BuildCondBr(guardTest, body, next);
                    }
                }

                _builder.PositionAtEnd(body);
                if (!LowerBranch(c.Body, wantValue, type, end, incoming)) return false;

                _builder.PositionAtEnd(next);
                if (last && wantValue) _builder.BuildUnreachable();
            }
        }
        finally
        {
            _itValues.Pop();
        }

        return FinishMerge(wantValue, lowered, end, incoming, "match.val", out value);
    }

    // The result is a Bool (i8) so patterns can be combined
    private bool TryLowerPattern(PatternNode pattern, LLVMValueRef target, AeroType targetType, SourceSpan span, out LLVMValueRef test)
    {
        test = default;

        switch (pattern)
        {
            case ElsePattern:
                test = LLVMValueRef.CreateConstInt(_context.Int8Type, 1);
                return true;

            case ConstantPattern c:
                if (!TryLowerValue(c.Value, out var constant)) return false;
                return TryEmitComparison(Operator.Equality, target, targetType, constant, _typed.TypeOf(c.Value) ?? targetType, c.Span, out test);

            case RangePattern r:
                return TryLowerRangeTest(r.Range, target, targetType, out test);

            case OrPattern o:
                // Both sides are evaluated, patterns have no side effects worth short-circuiting
                if (!TryLowerPattern(o.Left, target, targetType, span, out var left)) return false;
                if (!TryLowerPattern(o.Right, target, targetType, span, out var right)) return false;
                test = _builder.BuildOr(left, right, "or");
                return true;

            default:
                Fail("Type patterns are lowered together with user types (sub-task 6)", pattern.Span);
                return false;
        }
    }

    // a..b does not include b; a missing side is open
    private bool TryLowerRangeTest(RangeExpressionNode range, LLVMValueRef target, AeroType targetType, out LLVMValueRef test)
    {
        test = LLVMValueRef.CreateConstInt(_context.Int8Type, 1);

        if (range.Left is not null)
        {
            if (!TryLowerValue(range.Left, out var from)) return false;
            if (!TryEmitComparison(Operator.GreaterThanEqual, target, targetType, from, AeroType.Int, range.Span, out var above)) return false;
            test = above;
        }

        if (range.Right is not null)
        {
            if (!TryLowerValue(range.Right, out var to)) return false;
            if (!TryEmitComparison(Operator.LessThan, target, targetType, to, AeroType.Int, range.Span, out var below)) return false;
            test = _builder.BuildAnd(test, below, "range");
        }
        return true;
    }

    private bool TryLowerPatternTest(PatternTestExpressionNode p, out LLVMValueRef value)
    {
        value = default;

        if (!TryLowerValue(p.Target, out var target)) return false;
        var targetType = _typed.TypeOf(p.Target);
        if (targetType is null)
        {
            Fail("The tested expression was not resolved", p.Span);
            return false;
        }

        if (!TryLowerPattern(p.Pattern, target, targetType, p.Span, out var test)) return false;

        value = p.Negated ? _builder.BuildXor(test, LLVMValueRef.CreateConstInt(_context.Int8Type, 1), "not") : test;
        return true;
    }

    // A Range value is { start, end }; open ranges only make sense as patterns
    private bool TryLowerRange(RangeExpressionNode r, out LLVMValueRef value)
    {
        value = default;

        if (r.Left is null || r.Right is null)
        {
            Fail("An open range can only be used in a pattern", r.Span);
            return false;
        }

        if (!TryLowerValue(r.Left, out var from) || !TryLowerValue(r.Right, out var to)) return false;

        var range = _builder.BuildInsertValue(LLVMValueRef.CreateConstNull(_types.RangeStruct), from, 0, "range");
        value = _builder.BuildInsertValue(range, to, 1, "range");
        return true;
    }

    private void LowerFor(ForExpressionNode f)
    {
        var collectionType = _typed.TypeOf(f.Collection);

        if (_typed.BindingOf(f.Item) is not LocalBinding { Variable: var item })
        {
            Fail($"The loop variable '{f.Item.Name}' was not resolved", f.Item.Span);
            return;
        }

        if (!TryLowerValue(f.Collection, out var collection)) return;

        LLVMTypeRef itemType;
        LLVMValueRef start, stop;
        Func<LLVMValueRef, LLVMValueRef> itemAt;

        if (IsKind(collectionType, AeroType.Range))
        {
            itemType = _context.Int32Type;
            start = _builder.BuildExtractValue(collection, 0, "start");
            stop = _builder.BuildExtractValue(collection, 1, "stop");
            itemAt = i => i;
        }
        else if (collectionType is ArrayType array)
        {
            if (!_types.TryLower(array.ElementType, _scope, f.Collection.Span, out itemType)) return;

            var data = _builder.BuildExtractValue(collection, 0, "data");
            var elementType = itemType;
            start = LLVMValueRef.CreateConstInt(_context.Int32Type, 0);
            stop = _builder.BuildExtractValue(collection, 1, "length");
            itemAt = i => _builder.BuildLoad2(elementType, _builder.BuildGEP2(elementType, data, new[] { i }, "elem"), "item");
        }
        else
        {
            Fail($"Cannot iterate over '{collectionType}'", f.Collection.Span);
            return;
        }

        // The counter is separate so the body can change the loop variable without touching the iteration
        var counter = EntryAlloca(_context.Int32Type, "for.i");
        var itemSlot = EntryAlloca(itemType, f.Item.Name);
        _locals[item] = new LocalSlot(itemSlot, itemType);
        _builder.BuildStore(start, counter);

        var condition = AppendBlock("for.cond");
        var body = AppendBlock("for.body");
        var step = AppendBlock("for.step");
        var end = AppendBlock("for.end");

        _builder.BuildBr(condition);

        _builder.PositionAtEnd(condition);
        var current = _builder.BuildLoad2(_context.Int32Type, counter, "i");
        var more = _builder.BuildICmp(LLVMIntPredicate.LLVMIntSLT, current, stop, "more");
        _builder.BuildCondBr(more, body, end);

        _builder.PositionAtEnd(body);
        _builder.BuildStore(itemAt(current), itemSlot);
        _loops.Push((end, step));
        LowerStatements(f.Body.Statements);
        _loops.Pop();
        BranchIfOpen(step);

        _builder.PositionAtEnd(step);
        var next = _builder.BuildAdd(_builder.BuildLoad2(_context.Int32Type, counter, "i"), LLVMValueRef.CreateConstInt(_context.Int32Type, 1), "next");
        _builder.BuildStore(next, counter);
        _builder.BuildBr(condition);

        _builder.PositionAtEnd(end);
    }
}