namespace Luft.AstBuilder.Ast;

internal abstract class AstVisitor<T>
{
    public T Visit(AstNode node) => node switch
    {
        ProgramNode n => VisitProgram(n),
        
        // DeclarationNodes
        FunctionDeclarationNode n => VisitFunction(n),
        ExtensionDeclarationNode n => VisitExtension(n),
        StructDeclarationNode n => VisitStruct(n),
        ClassDeclarationNode n => VisitClass(n),
        InterfaceDeclarationNode n => VisitInterface(n),
        PropertyDeclarationNode n => VisitProperty(n),
        ConstructorDeclarationNode n => VisitConstructor(n),
        DestructorDeclarationNode n => VisitDestructor(n),
        
        
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
    protected virtual T VisitExtension(ExtensionDeclarationNode node) => Default(node);
    protected virtual T VisitStruct(StructDeclarationNode node) => Default(node);
    protected virtual T VisitClass(ClassDeclarationNode node) => Default(node);
    protected virtual T VisitInterface(InterfaceDeclarationNode node) => Default(node);
    protected virtual T VisitProperty(PropertyDeclarationNode node) => Default(node);
    protected virtual T VisitConstructor(ConstructorDeclarationNode node) => Default(node);
    protected virtual T VisitDestructor(DestructorDeclarationNode node) => Default(node);

    // StatementNodes
    protected virtual T VisitBlock(BlockStatementNode node) => Default(node);
    
    // ExpressionNodes
    protected virtual T VisitAnnotation(AnnotationExpressionNode node) => Default(node);
    
}