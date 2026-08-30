using Luft.AstBuilder.Ast;

namespace Luft.AstBuilder;

public record ParamNode(VariableKind varKind, string Name, TypeRef Type, ExpressionNode? Initializer = null);