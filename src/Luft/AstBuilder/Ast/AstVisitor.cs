namespace Luft.AstBuilder.Ast;

internal abstract class AstVisitor<T>
{
    public T Visit(AstNode node) => node switch
    {
        FileNode n => VisitFile(n),
        
        // DeclarationNodes
        FunctionDeclarationNode n => VisitFunction(n),
        ExtensionDeclarationNode n => VisitExtension(n),
        ExtensionBlockDeclarationNode n => VisitExtensionBlock(n),
        StructDeclarationNode n => VisitStruct(n),
        RecordDeclarationNode n => VisitRecord(n),
        AnnotationDeclarationNode n => VisitAnnotationDecl(n),
        ClassDeclarationNode n => VisitClass(n),
        TraitDeclarationNode n => VisitTrait(n),
        EnumMemberNode n => VisitEnumMember(n),
        EnumDeclarationNode n => VisitEnum(n),
        PropertyDeclarationNode n => VisitProperty(n),
        ConstructorDeclarationNode n => VisitConstructor(n),
        DestructorDeclarationNode n => VisitDestructor(n),
        ModuleDeclarationNode n => VisitModule(n),
        
        
        // StatementNodes
        BlockStatementNode n => VisitBlock(n),
        AnnotationNode n => VisitAnnotation(n),
        VariableStatementNode n => VisitVariable(n),
        ReturnStatementNode n => VisitReturn(n),
        BreakStatementNode n => VisitBreak(n),
        ContinueStatementNode n => VisitContinue(n),
        WhileStatementNode n => VisitWhile(n),
        ExpressionStatementNode n => VisitExpression(n),
        ImportStatementNode n => VisitImport(n),
        
        // ExpressionNodes
        IfExpressionNode n => VisitIf(n),
        ForExpressionNode n => VisitFor(n),
        ForInExpressionNode n => VisitForIn(n),
        MatchExpressionNode n => VisitMatch(n),
        CaseExpressionNode n => VisitCase(n),
        LiteralExpressionNode n => VisitLiteral(n),
        IdentifierExpressionNode n => VisitIdentifier(n),
        MemberAccessExpressionNode n => VisitMemberAccess(n),
        CallExpressionNode n => VisitCall(n),
        IndexExpressionNode n => VisitIndex(n),
        BinaryExpressionNode n => VisitBinary(n),
        UnaryExpressionNode n => VisitUnary(n),
        AssignmentExpressionNode n => VisitAssignment(n),
        SpawnExpressionNode n => VisitSpawn(n),
        LambdaExpressionNode n => VisitLambda(n),
        StringInterpolationExpressionNode n => VisitInterpolation(n),
        
        _ => Default(node)
    };
    
    
    // The default action for all non overridden nodes
    protected abstract T Default(AstNode ast);
    protected virtual T VisitFile(FileNode node) => Default(node);

    // DeclarationNodes
    protected virtual T VisitFunction(FunctionDeclarationNode node) => Default(node);
    protected virtual T VisitExtension(ExtensionDeclarationNode node) => Default(node);
    protected virtual T VisitExtensionBlock(ExtensionBlockDeclarationNode node) => Default(node);
    protected virtual T VisitStruct(StructDeclarationNode node) => Default(node);
    protected virtual T VisitRecord(RecordDeclarationNode node) => Default(node);
    protected virtual T VisitAnnotationDecl(AnnotationDeclarationNode node) => Default(node);
    protected virtual T VisitClass(ClassDeclarationNode node) => Default(node);
    protected virtual T VisitTrait(TraitDeclarationNode node) => Default(node);
    protected virtual T VisitEnumMember(EnumMemberNode node) => Default(node);
    protected virtual T VisitEnum(EnumDeclarationNode node) => Default(node);
    protected virtual T VisitProperty(PropertyDeclarationNode node) => Default(node);
    protected virtual T VisitConstructor(ConstructorDeclarationNode node) => Default(node);
    protected virtual T VisitDestructor(DestructorDeclarationNode node) => Default(node);
    protected virtual T VisitModule(ModuleDeclarationNode node) => Default(node);

    // StatementNodes
    protected virtual T VisitBlock(BlockStatementNode node) => Default(node);
    protected virtual T VisitAnnotation(AnnotationNode node) => Default(node);
    protected virtual T VisitVariable(VariableStatementNode node) => Default(node);
    protected virtual T VisitReturn(ReturnStatementNode node) => Default(node);
    protected virtual T VisitBreak(BreakStatementNode node) => Default(node);
    protected virtual T VisitContinue(ContinueStatementNode node) => Default(node);
    protected virtual T VisitWhile(WhileStatementNode node) => Default(node);
    protected virtual T VisitExpression(ExpressionStatementNode node) => Default(node);
    protected virtual T VisitImport(ImportStatementNode node) => Default(node);
    
    // ExpressionNodes
    protected virtual T VisitIf(IfExpressionNode node) => Default(node);
    protected virtual T VisitFor(ForExpressionNode node) => Default(node);
    protected virtual T VisitForIn(ForInExpressionNode node) => Default(node);
    protected virtual T VisitMatch(MatchExpressionNode node) => Default(node);
    protected virtual T VisitCase(CaseExpressionNode node) => Default(node);
    protected virtual T VisitLiteral(LiteralExpressionNode node) => Default(node);
    protected virtual T VisitIdentifier(IdentifierExpressionNode node) => Default(node);
    protected virtual T VisitMemberAccess(MemberAccessExpressionNode node) => Default(node);
    protected virtual T VisitCall(CallExpressionNode node) => Default(node);
    protected virtual T VisitIndex(IndexExpressionNode node) => Default(node);
    protected virtual T VisitBinary(BinaryExpressionNode node) => Default(node);
    protected virtual T VisitUnary(UnaryExpressionNode node) => Default(node);
    protected virtual T VisitAssignment(AssignmentExpressionNode node) => Default(node);
    protected virtual T VisitSpawn(SpawnExpressionNode node) => Default(node);
    protected virtual T VisitLambda(LambdaExpressionNode node) => Default(node);
    protected virtual T VisitInterpolation(StringInterpolationExpressionNode node) => Default(node);
}