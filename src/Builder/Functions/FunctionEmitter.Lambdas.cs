using LLVMSharp.Interop;
using Luft.Ast.Nodes;
using Luft.TypeChecker;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.Builder;

// A lambda is { function, environment }: the function takes the environment first and the environment holds a copy of
// every captured variable (and self when there is one), so assigning a captured variable inside the lambda is rejected by the checker
public sealed partial class FunctionEmitter
{
    private static int _lambdaCount;

    private bool TryLowerLambda(LambdaExpressionNode lambda, out LLVMValueRef value)
    {
        value = default;

        if (_typed.TypeOf(lambda) is not LambdaType type || type.ReturnType.IsAuto || type.Parameters.Any(p => p.Type.IsAuto))
        {
            Fail("The parameter types of this lambda could not be inferred, name them or pass the lambda where a lambda type is expected", lambda.Span);
            return false;
        }

        var captured = new List<(VariableSymbol Variable, LocalSlot Slot)>();
        foreach (var variable in _typed.Captures.GetValueOrDefault(lambda) ?? [])
        {
            if (!_locals.TryGetValue(variable, out var slot))
            {
                Fail($"'{variable.Name}' cannot be captured here", lambda.Span);
                return false;
            }
            captured.Add((variable, slot));
        }

        if (!TryLambdaFunctionType(type, lambda.Span, out var functionType)) return false;

        var function = _module.AddFunction($"<lambda>.{_lambdaCount++}", functionType);
        function.Linkage = LLVMLinkage.LLVMInternalLinkage;

        var hasSelf = _self.Handle != IntPtr.Zero;
        var elements = new List<LLVMTypeRef>();
        if (hasSelf) elements.Add(_types.Ptr);
        elements.AddRange(captured.Select(c => c.Slot.Type));

        LLVMTypeRef environmentType = default;
        LLVMValueRef environment = LLVMValueRef.CreateConstPointerNull(_types.Ptr);

        if (elements.Count > 0)
        {
            environmentType = LLVMTypeRef.CreateStruct(elements.ToArray(), false);
            environment = CallRuntime("aero_alloc", _types.Ptr, environmentType.SizeOf);

            var index = 0u;
            if (hasSelf) _builder.BuildStore(_self, _builder.BuildStructGEP2(environmentType, environment, index++, "self"));
            foreach (var (variable, slot) in captured)
            {
                var current = _builder.BuildLoad2(slot.Type, slot.Address, variable.Name);
                _builder.BuildStore(current, _builder.BuildStructGEP2(environmentType, environment, index++, variable.Name));
            }
        }

        using var emitter = new FunctionEmitter(_context, _module, _types, _typed, _functions, _constructors, _members, _strings);
        emitter.EmitLambda(lambda, type, function, environmentType, captured, hasSelf, _selfOwner, _selfReceiver, _scope);
        if (emitter.HasErrors) HasErrors = true;

        var result = _builder.BuildInsertValue(LLVMValueRef.CreateConstNull(_types.LambdaStruct), function, 0, "lambda");
        value = _builder.BuildInsertValue(result, environment, 1, "lambda");
        return true;
    }

    private void EmitLambda(LambdaExpressionNode lambda, LambdaType type, LLVMValueRef function, LLVMTypeRef environmentType,
        List<(VariableSymbol Variable, LocalSlot Slot)> captured, bool hasSelf, TypeSymbol? selfOwner, AeroType? selfReceiver, TypeScope scope)
    {
        _function = function;
        _scope = scope;
        _returnType = type.ReturnType;

        _entry = _context.AppendBasicBlock(function, "entry");
        _builder.PositionAtEnd(_entry);

        var environment = function.GetParam(0);
        var index = 0u;

        if (hasSelf)
        {
            _self = _builder.BuildLoad2(_types.Ptr, _builder.BuildStructGEP2(environmentType, environment, index++, "self.slot"), "self");
            _selfOwner = selfOwner;
            _selfReceiver = selfReceiver;
        }

        foreach (var (variable, slot) in captured)
        {
            var copy = EntryAlloca(slot.Type, variable.Name);
            var loaded = _builder.BuildLoad2(slot.Type, _builder.BuildStructGEP2(environmentType, environment, index++, variable.Name), variable.Name);
            _builder.BuildStore(loaded, copy);
            _locals[variable] = new LocalSlot(copy, slot.Type);
        }

        for (var i = 0; i < lambda.Parameters.Count; i++)
        {
            var declared = lambda.Parameters[i];
            var argument = function.GetParam((uint)i + 1);

            if (_typed.BindingOf(declared) is not LocalBinding { Variable: var variable })
            {
                Fail($"The parameter '{declared.Name}' was not resolved", declared.Span);
                return;
            }
            if (!_types.TryLower(type.Parameters[i].Type with { IsRef = false }, _scope, declared.Span, out var lowered)) return;

            argument.Name = declared.Name;
            var slot = EntryAlloca(lowered, declared.Name);
            _builder.BuildStore(argument, slot);
            _locals[variable] = new LocalSlot(slot, lowered);
        }

        // Without a named parameter the one parameter of the target type is 'it'
        if (lambda.Parameters.Count == 0 && type.Parameters.Count == 1)
        {
            var argument = function.GetParam(1);
            argument.Name = "it";
            _itValues.Push(argument);
        }

        LowerStatements(lambda.Body.Statements, true);

        if (IsTerminated()) return;
        if (_returnType == AeroType.Void) _builder.BuildRetVoid();
        else _builder.BuildUnreachable();
    }
}
