namespace Luft.AstBuilder.Ast;

internal abstract class AstVisitor<T>
{
    public T Visit(AstNode node) => node switch
    {
        ProgramNode n => VisitProgram(n),
        
        // DeclarationNodes
        FunctionDeclarationNode n => VisitFunction(n),
        
        // StatementNodes
        BlockStatementNode n => VisitBlock(n),
        
        // ExpressionNodes
        AnnotationExpressionNode n => VisitAnnotation(n),
        
        
        _ => Default(node)
    };
    
    
    // The default action for all non overridden nodes
    protected abstract T Default(AstNode ast);
    protected virtual T VisitProgram(ProgramNode node) => Default(node);

    // DeclarationNodes
    protected virtual T VisitFunction(FunctionDeclarationNode node) => Default(node);

    // StatementNodes
    protected virtual T VisitBlock(BlockStatementNode node) => Default(node);
    
    // ExpressionNodes
    protected virtual T VisitAnnotation(AnnotationExpressionNode node) => Default(node);
    
}