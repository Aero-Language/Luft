using Luft.Utility;

namespace Luft.Ast.Nodes;

public abstract class AstVisitor : AeroThrower<SourceSpan>
{
    protected void Visit(AstNode node)
    {
        switch (node)
        {
            // Special
            case FileNode n: VisitFile(n); break;
            case ParamNode n: VisitParam(n); break;
            case PropertyAccessorNode n: VisitPropertyAccessor(n); break;
            
            // DeclarationNodes
            case FunctionDeclarationNode n: VisitFunction(n); break;
            case ExtensionDeclarationNode n: VisitExtension(n); break;
            case ExtensionBlockDeclarationNode n: VisitExtensionBlock(n); break;
            case StructDeclarationNode n: VisitStruct(n); break;
            case RecordDeclarationNode n: VisitRecord(n); break;
            case AnnotationDeclarationNode n: VisitAnnotationDecl(n); break;
            case ClassDeclarationNode n: VisitClass(n); break;
            case TraitDeclarationNode n: VisitTrait(n); break;
            case EnumMemberNode n: VisitEnumMember(n); break;
            case EnumDeclarationNode n: VisitEnum(n); break;
            case PropertyDeclarationNode n: VisitProperty(n); break;
            case FieldDeclarationNode n: VisitField(n); break;
            case PrimaryConstructorDeclarationNode n: VisitPrimaryConstructor(n); break;
            case ConstructorDeclarationNode n: VisitConstructor(n); break;
            case DestructorDeclarationNode n: VisitDestructor(n); break;
            case ModuleDeclarationNode n: VisitModule(n); break;
            
            // StatementNodes
            case AnnotationStatementNode n: VisitAnnotation(n); break;
            case VariableStatementNode n: VisitVariable(n); break;
            case ReturnStatementNode n: VisitReturn(n); break;
            case BreakStatementNode n: VisitBreak(n); break;
            case ContinueStatementNode n: VisitContinue(n); break;
            case WhileStatementNode n: VisitWhile(n); break;
            case ExpressionStatementNode n: VisitExpression(n); break;
            case ImportStatementNode n: VisitImport(n); break;
            case AssignmentStatementNode n: VisitAssignment(n); break;
            
            // ExpressionNodes
            case BlockExpressionNode n: VisitBlock(n); break;
            case IfExpressionNode n: VisitIf(n); break;
            case ForExpressionNode n: VisitFor(n); break;
            case MatchExpressionNode n: VisitMatch(n); break;
            case CaseExpressionNode n: VisitCase(n); break;
            case LiteralExpressionNode n: VisitLiteral(n); break;
            case ArrayLiteralExpressionNode n: VisitArrayLiteral(n); break;
            case IdentifierExpressionNode n: VisitIdentifier(n); break;
            case MemberAccessExpressionNode n: VisitMemberAccess(n); break;
            case CallExpressionNode n: VisitCall(n); break;
            case IndexExpressionNode n: VisitIndex(n); break;
            case RangeExpressionNode n: VisitRange(n); break;
            case BinaryExpressionNode n: VisitBinary(n); break;
            case UnaryExpressionNode n: VisitUnary(n); break;
            case LambdaExpressionNode n: VisitLambda(n); break;
            case StringInterpolationExpressionNode n: VisitInterpolation(n); break;
            case ConcurrentExpressionNode n: VisitConcurrent(n); break;
            case SpawnExpressionNode n: VisitSpawn(n); break;
            case ScopedExpressionNode n: VisitScoped(n); break;
            
            default: Default(node); break;
        };
    }
    
    
    // The default action for all non overridden nodes
    protected virtual void Default(AstNode ast) {}
    
    // Special
    protected virtual void VisitFile(FileNode node) => Default(node);
    protected virtual void VisitParam(ParamNode node) => Default(node);
    protected virtual void VisitPropertyAccessor(PropertyAccessorNode node) => Default(node);
    

    // DeclarationNodes
    protected virtual void VisitFunction(FunctionDeclarationNode node) => Default(node);
    protected virtual void VisitExtension(ExtensionDeclarationNode node) => Default(node);
    protected virtual void VisitExtensionBlock(ExtensionBlockDeclarationNode node) => Default(node);
    protected virtual void VisitStruct(StructDeclarationNode node) => Default(node);
    protected virtual void VisitRecord(RecordDeclarationNode node) => Default(node);
    protected virtual void VisitAnnotationDecl(AnnotationDeclarationNode node) => Default(node);
    protected virtual void VisitClass(ClassDeclarationNode node) => Default(node);
    protected virtual void VisitTrait(TraitDeclarationNode node) => Default(node);
    protected virtual void VisitEnumMember(EnumMemberNode node) => Default(node);
    protected virtual void VisitEnum(EnumDeclarationNode node) => Default(node);
    protected virtual void VisitProperty(PropertyDeclarationNode node) => Default(node);
    protected virtual void VisitField(FieldDeclarationNode node) => Default(node);
    protected virtual void VisitPrimaryConstructor(PrimaryConstructorDeclarationNode node) => Default(node);
    protected virtual void VisitConstructor(ConstructorDeclarationNode node) => Default(node);
    protected virtual void VisitDestructor(DestructorDeclarationNode node) => Default(node);
    protected virtual void VisitModule(ModuleDeclarationNode node) => Default(node);

    // StatementNodes
    protected virtual void VisitBlock(BlockExpressionNode node) => Default(node);
    protected virtual void VisitAnnotation(AnnotationStatementNode statementNode) => Default(statementNode);
    protected virtual void VisitVariable(VariableStatementNode node) => Default(node);
    protected virtual void VisitReturn(ReturnStatementNode node) => Default(node);
    protected virtual void VisitBreak(BreakStatementNode node) => Default(node);
    protected virtual void VisitContinue(ContinueStatementNode node) => Default(node);
    protected virtual void VisitWhile(WhileStatementNode node) => Default(node);
    protected virtual void VisitExpression(ExpressionStatementNode node) => Default(node);
    protected virtual void VisitImport(ImportStatementNode node) => Default(node);
    
    // ExpressionNodes
    protected virtual void VisitIf(IfExpressionNode node) => Default(node);
    protected virtual void VisitFor(ForExpressionNode node) => Default(node);
    protected virtual void VisitMatch(MatchExpressionNode node) => Default(node);
    protected virtual void VisitCase(CaseExpressionNode node) => Default(node);
    protected virtual void VisitLiteral(LiteralExpressionNode node) => Default(node);
    protected virtual void VisitArrayLiteral(ArrayLiteralExpressionNode node) => Default(node);
    protected virtual void VisitIdentifier(IdentifierExpressionNode node) => Default(node);
    protected virtual void VisitMemberAccess(MemberAccessExpressionNode node) => Default(node);
    protected virtual void VisitCall(CallExpressionNode node) => Default(node);
    protected virtual void VisitIndex(IndexExpressionNode node) => Default(node);
    protected virtual void VisitBinary(BinaryExpressionNode node) => Default(node);
    protected virtual void VisitRange(RangeExpressionNode node) => Default(node);
    protected virtual void VisitUnary(UnaryExpressionNode node) => Default(node);
    protected virtual void VisitAssignment(AssignmentStatementNode node) => Default(node);
    protected virtual void VisitLambda(LambdaExpressionNode node) => Default(node);
    protected virtual void VisitInterpolation(StringInterpolationExpressionNode node) => Default(node);
    protected virtual void VisitConcurrent(ConcurrentExpressionNode node) => Default(node);
    protected virtual void VisitSpawn(SpawnExpressionNode node) => Default(node);
    protected virtual void VisitScoped(ScopedExpressionNode node) => Default(node);
}

public abstract class AstVisitor<T> : AeroThrower<SourceSpan>
{
    protected T Visit(AstNode node) => node switch
    {
        // Special
        FileNode n => VisitFile(n),
        ParamNode n => VisitParam(n),
        PropertyAccessorNode n => VisitPropertyAccessor(n),
        
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
        FieldDeclarationNode n => VisitField(n),
        PrimaryConstructorDeclarationNode n => VisitPrimaryConstructor(n),
        ConstructorDeclarationNode n => VisitConstructor(n),
        DestructorDeclarationNode n => VisitDestructor(n),
        ModuleDeclarationNode n => VisitModule(n),
        
        // StatementNodes
        AnnotationStatementNode n => VisitAnnotation(n),
        VariableStatementNode n => VisitVariable(n),
        ReturnStatementNode n => VisitReturn(n),
        BreakStatementNode n => VisitBreak(n),
        ContinueStatementNode n => VisitContinue(n),
        WhileStatementNode n => VisitWhile(n),
        ExpressionStatementNode n => VisitExpression(n),
        ImportStatementNode n => VisitImport(n),
        AssignmentStatementNode n => VisitAssignment(n),
        
        // ExpressionNodes
        BlockExpressionNode n => VisitBlock(n),
        IfExpressionNode n => VisitIf(n),
        ForExpressionNode n => VisitFor(n),
        MatchExpressionNode n => VisitMatch(n),
        CaseExpressionNode n => VisitCase(n),
        LiteralExpressionNode n => VisitLiteral(n),
        ArrayLiteralExpressionNode n => VisitArrayLiteral(n),
        IdentifierExpressionNode n => VisitIdentifier(n),
        MemberAccessExpressionNode n => VisitMemberAccess(n),
        CallExpressionNode n => VisitCall(n),
        IndexExpressionNode n => VisitIndex(n),
        RangeExpressionNode n => VisitRange(n),
        BinaryExpressionNode n => VisitBinary(n),
        UnaryExpressionNode n => VisitUnary(n),
        LambdaExpressionNode n => VisitLambda(n),
        StringInterpolationExpressionNode n => VisitInterpolation(n),
        ConcurrentExpressionNode n => VisitConcurrent(n),
        SpawnExpressionNode n => VisitSpawn(n),
        ScopedExpressionNode n => VisitScoped(n),
        
        _ => Default(node)
    };
    
    
    // The default action for all non overridden nodes
    protected abstract T Default(AstNode ast);
    
    // Special
    protected virtual T VisitFile(FileNode node) => Default(node);
    protected virtual T VisitParam(ParamNode node) => Default(node);
    protected virtual T VisitPropertyAccessor(PropertyAccessorNode node) => Default(node);
    

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
    protected virtual T VisitField(FieldDeclarationNode node) => Default(node);
    protected virtual T VisitPrimaryConstructor(PrimaryConstructorDeclarationNode node) => Default(node);
    protected virtual T VisitConstructor(ConstructorDeclarationNode node) => Default(node);
    protected virtual T VisitDestructor(DestructorDeclarationNode node) => Default(node);
    protected virtual T VisitModule(ModuleDeclarationNode node) => Default(node);

    // StatementNodes
    protected virtual T VisitBlock(BlockExpressionNode node) => Default(node);
    protected virtual T VisitAnnotation(AnnotationStatementNode statementNode) => Default(statementNode);
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
    protected virtual T VisitMatch(MatchExpressionNode node) => Default(node);
    protected virtual T VisitCase(CaseExpressionNode node) => Default(node);
    protected virtual T VisitLiteral(LiteralExpressionNode node) => Default(node);
    protected virtual T VisitArrayLiteral(ArrayLiteralExpressionNode node) => Default(node);
    protected virtual T VisitIdentifier(IdentifierExpressionNode node) => Default(node);
    protected virtual T VisitMemberAccess(MemberAccessExpressionNode node) => Default(node);
    protected virtual T VisitCall(CallExpressionNode node) => Default(node);
    protected virtual T VisitIndex(IndexExpressionNode node) => Default(node);
    protected virtual T VisitBinary(BinaryExpressionNode node) => Default(node);
    protected virtual T VisitRange(RangeExpressionNode node) => Default(node);
    protected virtual T VisitUnary(UnaryExpressionNode node) => Default(node);
    protected virtual T VisitAssignment(AssignmentStatementNode node) => Default(node);
    protected virtual T VisitLambda(LambdaExpressionNode node) => Default(node);
    protected virtual T VisitInterpolation(StringInterpolationExpressionNode node) => Default(node);
    protected virtual T VisitConcurrent(ConcurrentExpressionNode node) => Default(node);
    protected virtual T VisitSpawn(SpawnExpressionNode node) => Default(node);
    protected virtual T VisitScoped(ScopedExpressionNode node) => Default(node);
}