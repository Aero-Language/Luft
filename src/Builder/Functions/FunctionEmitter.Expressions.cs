using System.Text;
using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker;
using Luft.Utility;

namespace Luft.Builder;

// Literals, locals, direct calls, operators, if/match, ranges, for over ranges, strings, casts and arrays so far. The rest is sub-task 5e onwards.
public sealed partial class FunctionEmitter
{
    // A Void call succeeds with a default value
    private bool TryLowerExpression(ExpressionNode expression, out LLVMValueRef value)
    {
        value = default;

        switch (expression)
        {
            case LiteralExpressionNode l: return TryLowerLiteral(l, out value);
            case IdentifierExpressionNode id: return TryLowerLocal(id, out value);
            case ScopedExpressionNode s: return TryLowerExpression(s.Scoped, out value);
            case CallExpressionNode c: return TryLowerCall(c, out value);
            case BinaryExpressionNode b: return TryLowerBinary(b, out value);
            case UnaryExpressionNode u: return TryLowerUnary(u, out value);
            case IfExpressionNode i: return TryLowerIf(i, out value);
            case MatchExpressionNode m: return TryLowerMatch(m, out value);
            case BlockExpressionNode bl: return TryLowerBlockValue(bl, out value);
            case PatternTestExpressionNode p: return TryLowerPatternTest(p, out value);
            case RangeExpressionNode r: return TryLowerRange(r, out value);
            case ArrayLiteralExpressionNode al: return TryLowerArrayLiteral(al, out value);
            case IndexExpressionNode ix: return TryLowerIndex(ix, out value);
            case StringInterpolationExpressionNode si: return TryLowerInterpolation(si, out value);
            case ForExpressionNode f:
                LowerFor(f);
                return true;
            default:
                Fail($"'{expression.GetType().Name}' is not lowered yet (sub-task 5)", expression.Span);
                return false;
        }
    }

    private bool TryLowerValue(ExpressionNode expression, out LLVMValueRef value)
    {
        if (!TryLowerExpression(expression, out value)) return false;
        if (value.Handle != IntPtr.Zero) return true;

        Fail("An expression without a value cannot be used here", expression.Span);
        return false;
    }

    private bool TryLowerLiteral(LiteralExpressionNode l, out LLVMValueRef value)
    {
        value = default;

        switch (l.LiteralType, l.Value)
        {
            case (TokenType.IntLiteral, int i):
                value = LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)(long)i, true);
                return true;
            case (TokenType.FloatLiteral, float f):
                value = LLVMValueRef.CreateConstReal(_context.FloatType, f);
                return true;
            case (TokenType.BooleanLiteral, bool b):
                value = LLVMValueRef.CreateConstInt(_context.Int8Type, b ? 1UL : 0UL);
                return true;
            case (TokenType.CharLiteral, char c):
                value = LLVMValueRef.CreateConstInt(_context.Int16Type, c);
                return true;
            case (TokenType.StringLiteral, string s):
                value = StringConstant(s);
                return true;
            case (TokenType.ItLiteral, _) when _itValues.Count > 0:
                value = _itValues.Peek();
                return true;
            case (TokenType.NullLiteral, _):
                value = LLVMValueRef.CreateConstPointerNull(_types.Ptr);
                return true;
            default:
                Fail($"The literal '{l.LiteralType}' is not lowered yet", l.Span);
                return false;
        }
    }

    // A String is { ptr to UTF-16 data, length in UTF-16 units }
    private LLVMValueRef StringConstant(string raw)
    {
        var text = Unescape(raw);

        if (!_strings.TryGetValue(text, out var data))
        {
            var units = text.Select(c => LLVMValueRef.CreateConstInt(_context.Int16Type, c)).ToArray();
            var array = LLVMValueRef.CreateConstArray(_context.Int16Type, units);

            data = _module.AddGlobal(array.TypeOf, $"str.{_strings.Count}");
            data.Initializer = array;
            data.IsGlobalConstant = true;
            data.Linkage = LLVMLinkage.LLVMPrivateLinkage;
            _strings[text] = data;
        }

        var length = LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)text.Length);
        return LLVMValueRef.CreateConstNamedStruct(_types.StringStruct, [data, length]);
    }

    private static string Unescape(string raw)
    {
        if (!raw.Contains('\\')) return raw;

        var result = new StringBuilder();
        for (var i = 0; i < raw.Length; i++)
        {
            if (raw[i] != '\\' || i + 1 >= raw.Length)
            {
                result.Append(raw[i]);
                continue;
            }

            i++;
            result.Append(raw[i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '0' => '\0', _ => raw[i] });
        }
        return result.ToString();
    }

    private bool TryLowerLocal(IdentifierExpressionNode id, out LLVMValueRef value)
    {
        value = default;

        if (_typed.BindingOf(id) is LocalBinding { Variable: var variable } && _locals.TryGetValue(variable, out var slot))
        {
            value = _builder.BuildLoad2(slot.Type, slot.Address, id.Name);
            return true;
        }

        Fail($"'{id.Name}' cannot be used as a value yet, only locals and parameters can", id.Span);
        return false;
    }

    private bool TryLowerCall(CallExpressionNode call, out LLVMValueRef value)
    {
        value = default;

        if (_typed.BindingOf(call) is not FunctionBinding { Function: var function })
        {
            Fail("This kind of call is not lowered yet (sub-task 5 and later)", call.Span);
            return false;
        }
        if (!_functions.TryGetValue(function, out var declared))
        {
            Fail($"'{function.Name}' cannot be called yet, only non generic module functions can", call.Span);
            return false;
        }

        var parameters = function.Parameters;
        var arguments = new List<LLVMValueRef>();
        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            if (parameter.Type.IsRef)
            {
                Fail("ref arguments are not lowered yet", call.Span);
                return false;
            }

            var source = i < call.Arguments.Count ? call.Arguments[i] : parameter.Initializer;
            if (source is null)
            {
                Fail($"The argument for '{parameter.Name}' is missing", call.Span);
                return false;
            }

            if (!TryLowerValue(source, out var argument)) return false;
            arguments.Add(Coerce(argument, _typed.TypeOf(source), parameter.Type, source.Span));
        }

        var isVoid = function.ReturnType == AeroType.Void;
        var result = _builder.BuildCall2(declared.Type, declared.Value, arguments.ToArray(), isVoid ? "" : "call");
        if (!isVoid) value = result;
        return true;
    }

    // Wraps into a nullable where the target needs it, everything else already matches
    private LLVMValueRef Coerce(LLVMValueRef value, AeroType? from, AeroType to, SourceSpan span)
    {
        if (from is null || value.Handle == IntPtr.Zero || !to.IsNullable || to.IsRef) return value;
        if (!_types.TryLower(to, _scope, span, out var lowered)) return value;

        if (from == AeroType.Null) return LLVMValueRef.CreateConstNull(lowered);

        // Nullable classes are plain pointers, nothing to wrap
        if (from.IsNullable || lowered.Kind == LLVMTypeKind.LLVMPointerTypeKind) return value;

        var wrapped = _builder.BuildInsertValue(LLVMValueRef.CreateConstNull(lowered), LLVMValueRef.CreateConstInt(_context.Int8Type, 1), 0, "some");
        return _builder.BuildInsertValue(wrapped, value, 1, "some");
    }
}