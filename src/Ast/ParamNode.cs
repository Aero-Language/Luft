using Luft.Ast.Nodes;
using Luft.Utility;

namespace Luft.Ast;

public record ParamNode 
(
    string Name, 
    AeroType Type, 
    SourceSpan Span, 
    ExpressionNode? Initializer = null, 
    VariableKind varKind = VariableKind.Val
) : AstNode(Span);