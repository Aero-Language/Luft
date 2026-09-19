using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Utility;

namespace Luft.TypeChecker.Symbols;

public sealed class VariableSymbol : SourceSymbol, ISignature
{
    public VariableSymbol(VariableStatementNode stm) : base(stm.Span)
    {
        Name = stm.Name;
        Type = stm.Type;
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
    
    public string Name { get; }
    public VariableKind VarKind { get; }
    public AeroType Type { get; }
    public ExpressionNode? Initializer { get; }
    
    
    public SymbolSignature Signature => new(Name, [], []);
}
