using System.Text;
using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

// Literals, locals, calls (direct, ref arguments, function references, lambda values), operators, if/match, ranges, for, strings, casts and arrays so far. The rest is sub-task 5f onwards.
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
            case MemberAccessExpressionNode m when _typed.BindingOf(m) is FunctionBinding { Function: var f }:
                return TryLowerFunctionReference(m, f, out value);
            case MemberAccessExpressionNode m when _typed.BindingOf(m) is FieldBinding:
                return TryLowerField(m, out value);
            case MemberAccessExpressionNode m when _typed.BindingOf(m) is PropertyBinding:
                return TryLowerProperty(m, out value);
            case MemberAccessExpressionNode m when _typed.BindingOf(m) is EnumMemberBinding { Enum: var enumeration, Name: var memberName }:
                return TryLowerEnumMember(m, enumeration, memberName, out value);
            case MemberAccessExpressionNode m when _typed.BindingOf(m) is EnumParamBinding:
                Fail("The parameters of enum members are not lowered yet", m.Span);
                return false;
            case RefExpressionNode r:
                Fail("'ref' can only be used as a call argument", r.Span);
                return false;
            case WithExpressionNode w: return TryLowerWith(w, out value);
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
            case (TokenType.ItLiteral, _) when _backingType.Handle != IntPtr.Zero: // inside an accessor 'it' is the backing value
                value = _builder.BuildLoad2(_backingType, _backing, "it");
                return true;
            case (TokenType.NullLiteral, _):
                value = LLVMValueRef.CreateConstPointerNull(_types.Ptr);
                return true;
            case (TokenType.SelfLiteral, _) when _self.Handle != IntPtr.Zero:
                return TryLowerSelf(l.Span, out value);
            default:
                Fail($"The literal '{l.LiteralType}' is not lowered yet", l.Span);
                return false;
        }
    }

    // An enum value is the ordinal of its member, values given to members are looked up separately
    private bool TryLowerEnumMember(ExpressionNode node, EnumSymbol enumeration, string name, out LLVMValueRef value)
    {
        value = default;

        var ordinal = enumeration.Members.FindIndex(m => m.Name == name);
        if (ordinal < 0)
        {
            Fail($"'{enumeration.Name}' has no member '{name}'", node.Span);
            return false;
        }

        value = LLVMValueRef.CreateConstInt(_context.Int32Type, (ulong)ordinal);
        return true;
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

        if (_typed.BindingOf(id) is FieldBinding) return TryLowerField(id, out value);
        if (_typed.BindingOf(id) is PropertyBinding) return TryLowerProperty(id, out value);
        if (_typed.BindingOf(id) is FunctionBinding { Function: var function }) return TryLowerFunctionReference(id, function, out value);

        Fail($"'{id.Name}' cannot be used as a value yet, only locals, parameters, fields and functions can", id.Span);
        return false;
    }

    private bool TryLowerCall(CallExpressionNode call, out LLVMValueRef value)
    {
        value = default;

        switch (_typed.BindingOf(call))
        {
            case FunctionBinding { Function: var function }:
                return TryLowerDirectCall(call, function, out value);
            case ValueCallBinding { Lambda: var lambda }:
                return TryLowerValueCall(call, lambda, out value);
            case ConstructorBinding binding:
                return TryLowerConstructorCall(call, binding, out value);
            default:
                Fail("This kind of call is not lowered yet (sub-task 6 and later)", call.Span);
                return false;
        }
    }

    private bool TryLowerDirectCall(CallExpressionNode call, FunctionSymbol function, out LLVMValueRef value)
    {
        value = default;

        if (!_functions.TryGetValue(function, out var declared))
        {
            Fail($"'{function.Name}' cannot be called yet, only non generic module functions can", call.Span);
            return false;
        }

        var parameters = function.Parameters;
        var arguments = new List<LLVMValueRef>();

        LLVMValueRef receiver = default;
        if (declared.HasSelf)
        {
            if (!TryLowerReceiver(call, declared.Owner!, out receiver)) return false;
            arguments.Add(receiver);
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            var parameter = parameters[i];
            var source = i < call.Arguments.Count ? call.Arguments[i] : parameter.Initializer;

            if (!TryLowerArgument(source, parameter.Type, parameter.Name, call.Span, out var argument)) return false;
            arguments.Add(argument);
        }

        // A virtual method on a class goes through the vtable of the object it is called on
        var callee = declared.Value;
        if (declared.Owner is ClassSymbol && _members.VirtualSlots.TryGetValue(function, out var slot))
            callee = LoadVirtualFunction(receiver, slot);

        var isVoid = function.ReturnType == AeroType.Void;
        var result = _builder.BuildCall2(declared.Type, callee, arguments.ToArray(), isVoid ? "" : "call");
        if (!isVoid) value = result;
        return true;
    }

    // A call through a lambda value: { function, environment }, the environment is passed first
    private bool TryLowerValueCall(CallExpressionNode call, LambdaType lambda, out LLVMValueRef value)
    {
        value = default;

        if (call.Arguments.Count != lambda.Parameters.Count)
        {
            Fail($"The lambda takes {lambda.Parameters.Count} arguments but got {call.Arguments.Count}", call.Span);
            return false;
        }
        if (!TryLowerValue(call.Target, out var callee)) return false;
        if (!TryLambdaFunctionType(lambda, call.Span, out var type)) return false;

        var arguments = new List<LLVMValueRef> { _builder.BuildExtractValue(callee, 1, "env") };
        for (var i = 0; i < lambda.Parameters.Count; i++)
        {
            var parameter = lambda.Parameters[i];
            if (!TryLowerArgument(call.Arguments[i], parameter.Type, parameter.Name, call.Span, out var argument)) return false;
            arguments.Add(argument);
        }

        var function = _builder.BuildExtractValue(callee, 0, "fn");
        var isVoid = lambda.ReturnType == AeroType.Void;
        var result = _builder.BuildCall2(type, function, arguments.ToArray(), isVoid ? "" : "call");
        if (!isVoid) value = result;
        return true;
    }

    // A ref parameter takes the address of its 'ref' argument, everything else takes the value
    private bool TryLowerArgument(ExpressionNode? source, AeroType type, string name, SourceSpan span, out LLVMValueRef argument)
    {
        argument = default;

        if (source is null)
        {
            Fail($"The argument for '{name}' is missing", span);
            return false;
        }

        if (type.IsRef)
        {
            if (source is RefExpressionNode r) return TryGetAddress(r.Operand, out argument, out _, out _);

            Fail($"The argument for the ref parameter '{name}' has to be passed with 'ref'", source.Span);
            return false;
        }

        if (!TryLowerValue(source, out var lowered)) return false;
        argument = Coerce(lowered, _typed.TypeOf(source), type, source.Span);
        return true;
    }

    // The function type behind a lambda value, the environment pointer comes first
    private bool TryLambdaFunctionType(LambdaType lambda, SourceSpan span, out LLVMTypeRef type)
    {
        type = default;
        if (!_types.TryLower(lambda.ReturnType, _scope, span, out var returnType)) return false;

        var parameters = new List<LLVMTypeRef> { _types.Ptr };
        foreach (var p in lambda.Parameters)
        {
            if (!_types.TryLower(p.Type, _scope, span, out var lowered)) return false;
            parameters.Add(lowered);
        }

        type = LLVMTypeRef.CreateFunction(returnType, parameters.ToArray(), false);
        return true;
    }

    // A function name as a value is { thunk, null }, so it calls like any other lambda
    private bool TryLowerFunctionReference(ExpressionNode node, FunctionSymbol function, out LLVMValueRef value)
    {
        value = default;

        if (!_functions.TryGetValue(function, out var declared))
        {
            Fail($"'{function.Name}' cannot be used as a value yet, only non generic module functions can", node.Span);
            return false;
        }

        if (declared.HasSelf)
        {
            Fail($"The method '{function.Name}' cannot be used as a value yet, it needs an object", node.Span);
            return false;
        }

        var thunk = FunctionThunk(declared);
        value = LLVMValueRef.CreateConstNamedStruct(_types.LambdaStruct, [thunk, LLVMValueRef.CreateConstPointerNull(_types.Ptr)]);
        return true;
    }

    // Takes the environment first and forwards the rest to the real function; one per function
    private LLVMValueRef FunctionThunk(DeclaredFunction declared)
    {
        var name = $"thunk.{declared.Value.Name}";
        var existing = _module.GetNamedFunction(name);
        if (existing.Handle != IntPtr.Zero) return existing;

        var original = declared.Type;
        var originalParameters = original.ParamTypes;
        var thunkType = LLVMTypeRef.CreateFunction(original.ReturnType, new[] { _types.Ptr }.Concat(originalParameters).ToArray(), false);

        var thunk = _module.AddFunction(name, thunkType);
        thunk.Linkage = LLVMLinkage.LLVMInternalLinkage;

        using var b = _context.CreateBuilder();
        b.PositionAtEnd(_context.AppendBasicBlock(thunk, "entry"));

        var forwarded = originalParameters.Select((_, i) => thunk.GetParam((uint)i + 1)).ToArray();
        var isVoid = original.ReturnType.Kind == LLVMTypeKind.LLVMVoidTypeKind;
        var call = b.BuildCall2(original, declared.Value, forwarded, isVoid ? "" : "call");

        if (isVoid) b.BuildRetVoid();
        else b.BuildRet(call);
        return thunk;
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