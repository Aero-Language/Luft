using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Utility;

namespace Luft.TypeChecker.Symbols;

public sealed class VariableSymbol : SourceSymbol, ISignature
{
    public VariableSymbol(VariableStatementNode stm) : this(stm, stm.Type) { }

    // Used once type inference has worked out a `var`/`const` declaration's real type instead of `<auto>`.
    public VariableSymbol(VariableStatementNode stm, AeroType resolvedType) : base(stm.Span)
    {
        Name = stm.Name;
        Type = resolvedType;
        VarKind = stm.VarKind;
        Initializer = stm.Initializer;
    }

    public VariableSymbol(ParamNode p) : base(p.Span)
    {
        Name = p.Name;
        Type = p.Type;
        VarKind = p.VarKind;
        Initializer = p.Initializer;
    }

    // For symbols the language implies rather than declares, e.g. a property setter's `value`.
    public VariableSymbol(string name, AeroType type, SourceSpan span) : base(span)
    {
        Name = name;
        Type = type;
        VarKind = VariableKind.Val;
        Initializer = null;
    }

    public string Name { get; }
    public VariableKind VarKind { get; }
    public AeroType Type { get; }
    public ExpressionNode? Initializer { get; }


    public SymbolSignature Signature => new(Name, [], []);
}