using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.Utility;

namespace Luft.Ast;

public sealed class AstBuilder : SafeCollectionIterator<Token, SourceSpan>
{
    private static readonly TokenType[] ExcludedTypes = [TokenType.Whitespace, TokenType.Comment, TokenType.Unknown];

    public AstBuilder()
    {
        Init((t, _) => t.Span, (t, _) => t.Type == TokenType.Eof, t => !ExcludedTypes.Contains(t.Type));
    }

    public FileNode BuildAst(Token[] rawTokens)
    {
        Start(rawTokens);
        
        return ConsumeFile();
    }
    
    
    // Special
    FileNode ConsumeFile()
    {
        var imports = new List<ImportStatementNode>();
        var modules = new List<ModuleDeclarationNode>();
        var globals = new List<DeclarationNode>();

        while (Peek().Type is not TokenType.Eof)
        {
            if (Peek().Type is TokenType.ImportKeyword or TokenType.FromKeyword)
            {
                imports.Add(ConsumeImport());
            }
            else if (Peek().Type is TokenType.ModuleKeyword)
            {
                modules.Add(ConsumeModule());
            }
            else
            {
                globals.Add(ConsumeDecl());
            }
        }

        return new FileNode(modules.ToArray(), imports.ToArray(), globals.ToArray(), Peek().Span);
    }
    AnnotationNode ConsumeAnnotation()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.At,"Expected '@' to start annotation.");
        
        var name = ExpectType(TokenType.Identifier, "Identifier not found.").Value;
        
        // Handle parameters if passed
        var parameters = new List<ExpressionNode>();
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            Consume(); // Consume '('
            while (Peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
            {
                parameters.Add(ConsumeExpression());
                
                if (Peek().Type is TokenType.Comma)
                {
                    Consume(); // Consume ','
                    
                    // Allow trailing comma: @Foo(a, b,)
                    if (Peek().Type is TokenType.ParenthesisClose)
                    {
                        break;
                    }
                }
                else if (Peek().Type is not TokenType.ParenthesisClose)
                {
                    Error("Expected ',' or ')' after parameter.", Peek().Span);
                    Consume();
                    break; 
                }
            }

            ExpectType(TokenType.ParenthesisClose, "Expected ')' to close annotation arguments.");
        }

        return new AnnotationNode(name, parameters.ToValueList(), startSpan.To(Peek(-1).Span.End));
    }
    
    
    // Declarations
    DeclarationNode ConsumeDecl()
    {
        var annotations = ConsumeAnnotations();
        var accessMod = ConsumeAccessMod();
        var memberMod = ConsumeMemberMod();
        var inheritance = ConsumeInheritance();
        
        return Peek().Value switch
        {
            "struct" => ConsumeStruct(annotations, accessMod, memberMod, inheritance),
            "record" => ConsumeRecord(annotations, accessMod, memberMod, inheritance),
            "class" => ConsumeClass(annotations, accessMod, memberMod, inheritance),
            "trait" => ConsumeTrait(annotations, accessMod, inheritance),
            "enum" => ConsumeEnum(annotations, accessMod),
            "annotation" => ConsumeAnnotationDecl(annotations, accessMod, memberMod),
            "fun" => ConsumeFunction(annotations, accessMod, memberMod, inheritance),
            "extension" => ConsumeExtension(annotations, accessMod, memberMod, inheritance),
            "extensions" => ConsumeExtensionBlock(accessMod),
            "constructor" => ConsumeConstructor(annotations, accessMod, memberMod),
            "destructor" => ConsumeDestructor(),
            _ => Peek().Type is TokenType.VariableKind ? ConsumeVariableDecl(annotations, accessMod, memberMod, inheritance) : ConsumeProperty(annotations, accessMod, memberMod, inheritance)
        };
    }
    ModuleDeclarationNode ConsumeModule()
    {
        ExpectType(TokenType.ModuleKeyword, "Use the 'module' keyword to declare a module.");
        var identifier= ConsumeIdentifier();
        
        var decls = new List<DeclarationNode>();
        if (Peek().Type is TokenType.BracketOpen)
        {
            Consume(); // Consume '{'
            
            while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
            {
                decls.Add(ConsumeDecl());
            }

            ExpectType(TokenType.BracketClose, "Expected '}'");
        }
        else
        {
            ConsumeStatementTerminator();
            
            while (Peek().Type is not TokenType.Eof)
            {
                decls.Add(ConsumeDecl());
            }
        }
        
        return new ModuleDeclarationNode(identifier, decls.ToArray(), Peek().Span);
    }
    FunctionDeclarationNode ConsumeFunction(ValueList<AnnotationNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.FunctionDefault;

        if (Peek().Type is TokenType.InstanceKind && Peek().Value == "extension") Consume(); // Consume 'extension' 
        ExpectInstance(["fun", "constructor", "destructor"], "Function keyword not found.");
        
        var name = ConsumeIdentifier();
        var generics = ConsumeGenericDecls();
        var parameters = ConsumeParameterDecl();

        var returning = AeroType.Void;
        if (Peek().Type is TokenType.ArrowSymbol)
        {
            Consume(); // Consume '->'
            returning = ConsumeType();
        }

        BlockExpressionNode? body = null;
        if (Peek().Type is TokenType.BracketOpen or TokenType.EqualArrow)
        {
            body = ConsumeBlock();
        }
        else if (IsStatementTerminator())
        {
            ConsumeStatementTerminator();
        }
        
        return new FunctionDeclarationNode(annotations.OrNew(), access, inheritance, memberMod, returning, name, generics.ToValueList(), parameters, body, startSpan.To(Peek().Span.End));
    }
    ExtensionDeclarationNode ConsumeExtension(ValueList<AnnotationNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        string targetType;
        DeclarationNode decl;
        var kind = Peek(1);
        if (kind.Type is TokenType.InstanceKind && kind.Value == "fun") // Peek() is 'extension', so check the next one
        {
            var node = ConsumeFunction(annotations, accessMod, memberMod, inheritance);
            decl = node;
            targetType = node.Name.FirstIdentifier();
        }
        else
        {
            var node = ConsumeProperty(annotations, accessMod, memberMod, inheritance);
            decl = node;
            targetType = node.Name.FirstIdentifier();
        }
        
        return new ExtensionDeclarationNode(decl, targetType.ToType(), decl.Span);
    }
    ExtensionBlockDeclarationNode ConsumeExtensionBlock(AccessMod? accessMod)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.ExtensionDefault;
        
        ExpectInstance(["extensions"], "Expected 'extensions'");
        
        var target = ConsumeType();
        
        ExpectType(TokenType.BracketOpen, "Expected '{'");
        List<ExtensionDeclarationNode> extensions = [];
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            var declStart = Peek().Span;
            var decl = ConsumeDecl();
            extensions.Add(new ExtensionDeclarationNode(decl, target, declStart.To(Peek().Span)));
        }
        ExpectType(TokenType.BracketClose, "Expected '}'");
        
        return new ExtensionBlockDeclarationNode(access, target, extensions.ToValueList(), startSpan.To(Peek().Span));
    }
    StructDeclarationNode ConsumeStruct(ValueList<AnnotationNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.StructDeclDefault;
        
        // Make sure the struct keyword was used
        ExpectInstance(["struct"], "Expected 'struct'");

        var name = ConsumeIdentifier();
        
        List<DeclarationNode> decls = [];
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            decls.Add(ConsumePrimaryConstructor());
        }

        var implementations = ConsumeImplementations();

        ExpectType(TokenType.BracketOpen, "Expect '{'");
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            decls.Add(ConsumeDecl());
        }
        ExpectType(TokenType.BracketClose, "Expect '}'");
        
        return new StructDeclarationNode(annotations.OrNew(), access, inheritance, memberMod, name, decls.ToValueList(), implementations, startSpan.To(Peek().Span));
    }
    RecordDeclarationNode ConsumeRecord(ValueList<AnnotationNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.RecordDeclDefault;

        // Make sure the record keyword was used
        ExpectInstance(["record"], "Expected 'record'");
        
        var name = ConsumeIdentifier();
        
        var generics = ConsumeGenericDecls();
        
        List<DeclarationNode> decls = [];
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            decls.Add(ConsumePrimaryConstructor());
        }

        var implementations = ConsumeImplementations();
        
        ExpectType(TokenType.BracketOpen, "Expected '{'");
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            decls.Add(ConsumeDecl());
        }
        ExpectType(TokenType.BracketClose, "Expected '}'");
        
        return new RecordDeclarationNode(annotations.OrNew(), access, inheritance, memberMod, name, generics, decls.ToValueList(), implementations, startSpan.To(Peek().Span));
    }
    AnnotationDeclarationNode ConsumeAnnotationDecl(ValueList<AnnotationNode>? annotations, AccessMod? accessMod, MemberMod memberMod)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.AnnotationDeclDefault;

        // Make sure the annotation keyword was used
        ExpectInstance(["annotation"], "Expected 'annotation'");
        
        var name = ConsumeIdentifier();
        
        var generics = ConsumeGenericDecls();
        
        List<DeclarationNode> decls = [];
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            decls.Add(ConsumePrimaryConstructor());
        }
        
        return new AnnotationDeclarationNode(annotations.OrNew(), access, memberMod, name, generics, decls.ToValueList(), startSpan.To(Peek().Span));
    }
    ClassDeclarationNode ConsumeClass(ValueList<AnnotationNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.ClassDeclDefault;

        // Make sure the class keyword was used
        ExpectInstance(["class"], "Expected 'class'");
        
        var name = ConsumeIdentifier();
        var generics = ConsumeGenericDecls();
        
        List<DeclarationNode> decls = [];
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            decls.AddRange(ConsumePrimaryConstructor());
        }

        var implementations = ConsumeImplementations();
        
        ExpectType(TokenType.BracketOpen, "Expect '{'");
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            decls.Add(ConsumeDecl());
        }
        ExpectType(TokenType.BracketClose, "Expect '}'");
        
        return new ClassDeclarationNode(annotations.OrNew(), access, inheritance, memberMod, name, generics, decls.ToValueList(), implementations, startSpan.To(Peek().Span));
    }
    TraitDeclarationNode ConsumeTrait(ValueList<AnnotationNode>? annotations, AccessMod? accessMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.TraitDeclDefault;

        // Make sure the class keyword was used
        ExpectInstance(["trait"], "Expected 'trait'");
        
        var name = ConsumeIdentifier();
        var generics = ConsumeGenericDecls();
        var implementations = ConsumeImplementations();

        ExpectType(TokenType.BracketOpen, "Expected '{'");
        List<DeclarationNode> decls = [];
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            decls.Add(ConsumeDecl());
        }
        ExpectType(TokenType.BracketClose, "Expected '}'");
        
        return new TraitDeclarationNode(annotations.OrNew(), access, inheritance, name, generics, decls.ToValueList(), implementations, startSpan.To(Peek().Span));
    }
    EnumDeclarationNode ConsumeEnum(ValueList<AnnotationNode>? annotations, AccessMod? accessMod)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.EnumDeclDefault;
        
        // Make sure the enum keyword was used and check if it's an enum class
        ExpectInstance(["enum"], "Expected 'enum'");
        bool isEnumClass = Peek().Type is TokenType.InstanceKind && Peek().Value == "class";
        if (isEnumClass) Consume(); // Consume 'class'

        var name = ConsumeIdentifier();

        ValueList<ParamNode>? memberValues = null;
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            memberValues = ConsumeParameterDecl();
        }
        
        AeroType? memberType = null;
        if (Peek().Type is TokenType.Colon)
        {
            Consume(); // Consume ':'
            memberType = ConsumeType();
        }

        ExpectType(TokenType.BracketOpen, "Expected '{'");
        List<EnumMemberNode> members = [];
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            var memberStartSpan = Peek().Span;
            
            var memberName = ConsumeIdentifier();
            ExpressionNode? memberValue = null;
            if (Peek().Type is TokenType.Assign)
            {
                Consume(); // Consume '='
                memberValue = ConsumeExpression();
            }
            members.Add(new EnumMemberNode(memberName, memberValue, memberStartSpan.To(Peek().Span)));
            
            if (Peek().Type is TokenType.Comma) Consume(); // Consume ','
        }
        ExpectType(TokenType.BracketClose, "Expected '}'");
        
        return new EnumDeclarationNode(annotations.OrNew(), access, name, memberType, memberValues, members.ToValueList(),  startSpan.To(Peek().Span));
    }
    PropertyDeclarationNode ConsumeProperty(ValueList<AnnotationNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.PropertyDefault;
        
        var name = ConsumeIdentifier();

        ExpectType(TokenType.Colon, "Expected ':'");

        var type = ConsumeType();

        ExpectType(TokenType.BracketOpen, "Expected '{'");

        PropertyAccessorNode? getter = null;
        PropertyAccessorNode? setter = null;
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            var accessorType = Peek().Type;
            
            if (accessorType is TokenType.GetKeyword or TokenType.SetKeyword)
            {
                Consume(); // Consume 'get|set'
                
                if (accessorType is TokenType.GetKeyword && getter != null)
                {
                    Error("You can only declare one getter for a property.", Peek().Span);
                    continue;
                }
                if (accessorType is TokenType.SetKeyword && setter != null)
                {
                    Error("You can only declare one setter for a property.", Peek().Span);
                    continue;
                }
                
                var accessorStart = Peek().Span;
                
                var accessorAccess = ConsumeAccessMod() ?? AccessMod.Public;
                BlockExpressionNode? accessorBlock = null;
                if (!IsStatementTerminator()) accessorBlock = ConsumeBlock();
                else ConsumeStatementTerminator();
                
                var accessor = new PropertyAccessorNode(accessorAccess, accessorBlock, accessorStart);

                if (accessorType is TokenType.SetKeyword) setter = accessor;
                else getter = accessor;
            }
            else
            {
                Error("You can only declare Property accessors here.", Peek().Span);
                Consume(); // Consume the unknown token
            }
        }
        
        ExpectType(TokenType.BracketClose, "Expected '}'");
        
        ExpressionNode? init = null;
        if (Peek().Type is TokenType.Assign)
        {
            Consume(); // Consume '='
            init = ConsumeExpression();
        }
        
        return new PropertyDeclarationNode(annotations.OrNew(), access, inheritance, memberMod, type, name, getter, setter, init, startSpan.To(Peek().Span));
    }
    VariableDeclarationNode ConsumeVariableDecl(ValueList<AnnotationNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.VariableDefault;
        
        var varKind = ConsumeVarKind() ?? VariableKind.Val;
        var name = ConsumeIdentifier();

        var type = AeroType.Auto;
        if (Peek().Type is TokenType.Colon)
        {
            Consume(); // Consume ':'
            type = ConsumeType();
        }

        ExpressionNode? init = null;
        if (Peek().Type is TokenType.Assign)
        {
            Consume(); // Consume '='
            init = ConsumeExpression();
        }
        
        return new VariableDeclarationNode(annotations.OrNew(), access, inheritance, memberMod, varKind, type, name, init, startSpan.To(Peek().Span));
    }
    PrimaryConstructorDeclarationNode ConsumePrimaryConstructor()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");

        var variables = new List<VariableDeclarationNode>();
        while (Peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
        {
            var paramStart = Peek().Span;
            
            var varKind = ConsumeVarKind() ?? VariableKind.Val;
            var name = ConsumeIdentifier();

            ExpectType(TokenType.Colon, "Expected ':'");
            
            var type = ConsumeType();
            
            ExpressionNode? init = null;
            if (Peek().Type is TokenType.Equality)
            {
                Consume(); // Consume '='
                init = ConsumeExpression();
            }
            
            variables.Add(new VariableDeclarationNode([], AccessMod.Private, InheritanceMod.None, MemberMod.None, varKind, type, name, init, paramStart.To(Peek().Span)));
            
            if (Peek().Type is TokenType.Comma) Consume(); // Consume ','
        }

        ExpectType(TokenType.ParenthesisClose, "Expected ')'");

        var endSpan = startSpan.To(Peek().Span);
        return new PrimaryConstructorDeclarationNode([], AccessMod.Public, variables.ToValueList(), [], new BlockExpressionNode([], endSpan), endSpan);
    }
    ConstructorDeclarationNode ConsumeConstructor(ValueList<AnnotationNode>? annotations, AccessMod? accessMod, MemberMod memberMod)
    {
        var fun = ConsumeFunction(annotations, accessMod, memberMod, InheritanceMod.None);
        return new ConstructorDeclarationNode(fun.Annotations, fun.AccessMod, fun.Parameters, fun.Body, fun.Span);
    }
    DestructorDeclarationNode ConsumeDestructor()
    {
        var fun = ConsumeFunction([], AccessMod.Private, MemberMod.None, InheritanceMod.None);
        return new DestructorDeclarationNode(fun.Annotations, fun.Body, fun.Span);
    }
    
    
    // Statements
    StatementNode ConsumeStatement()
    {
        var type = Peek().Type;
        switch (type)
        {
            case TokenType.VariableKind:
                return ConsumeVariable();
            case TokenType.ReturnKeyword:
                return ConsumeReturn();
            case TokenType.WhileKeyword:
                return ConsumeWhile();
            case TokenType.BreakKeyword or TokenType.ContinueKeyword:
                return ConsumeKeyword();
        }
        
        return ConsumeExpressionStatement();
    }
    VariableStatementNode ConsumeVariable()
    {
        var startSpan = Peek().Span;
        
        var varKindNull = ConsumeVarKind();
        if (varKindNull is null) Error("You have to specify the variable declaration kind ('const', 'val', 'var')", startSpan);
        var varKind = varKindNull ?? VariableKind.Val;
        
        var name = ConsumeIdentifier();
        var type = AeroType.Auto;
        if (Peek().Type is TokenType.Colon)
        {
            Consume(); // Consume ':'
            type = ConsumeType();
        }
        
        ExpressionNode? init = null;
        if (Peek().Type is TokenType.Assign)
        {
            Consume(); // Consume '='
            init = ConsumeExpression();
        }
        
        ConsumeStatementTerminator();
        
        return new VariableStatementNode(varKind, type, name, init, startSpan.To(Peek().Span));
    }
    ReturnStatementNode ConsumeReturn()
    {
        var startSpan = Peek().Span;

        ExpectType(TokenType.ReturnKeyword, "Expected 'return'");

        ExpressionNode? val = null;
        if (!IsStatementTerminator()) val = ConsumeExpression();
        
        ConsumeStatementTerminator();
        
        return new ReturnStatementNode(val, startSpan.To(Peek().Span));
    }
    StatementNode ConsumeKeyword()
    {
        var span = Peek().Span;
        StatementNode statement = Peek().Type switch
        {
            TokenType.BreakKeyword => new BreakStatementNode(span),
            TokenType.ContinueKeyword => new ContinueStatementNode(span),
            _ => new EmptyStatementNode(span)
        };

        if (statement is not EmptyStatementNode) Consume(); // Consume the keyword

        ConsumeStatementTerminator();
        
        return statement;
    }
    WhileStatementNode ConsumeWhile()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.WhileKeyword, "Expected 'while'");
        
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");
        var condition = ConsumeExpression();
        ExpectType(TokenType.ParenthesisClose, "Expected ')'");
        
        var body = ConsumeBlock();
        
        ConsumeStatementTerminator();
        
        return new WhileStatementNode(condition, body, startSpan.To(Peek().Span));
    }
    StatementNode ConsumeExpressionStatement()
    {
        var startSpan = Peek().Span;
        
        var expression = ConsumeExpression();
        ConsumeStatementTerminator();
        
        if (expression is BinaryExpressionNode bin && bin.Operator.IsAssignment())
        {
            return new AssignmentStatementNode(bin.Left, bin.Operator, bin.Right, bin.Span);
        }
        
        return new ExpressionStatementNode(expression, startSpan.To(Peek().Span));
    }
    ImportStatementNode ConsumeImport()
    {
        var start = Peek().Span;

        bool isFrom = false;

        if (Peek().Type is TokenType.FromKeyword)
        {
            ExpectType(TokenType.FromKeyword, "From keyword not found.");
            isFrom = true;
        }
        else
        {
            ExpectType(TokenType.ImportKeyword, "ConsumeImport keyword not found.");
        }

        var identifier = ConsumeIdentifier();
        
        if (isFrom)
        {
            ExpectType(TokenType.ImportKeyword, "Import keyword not found.");

            List<string> subImports = [];
            while (Peek().Type is TokenType.Identifier and not TokenType.Eof)
            {
                subImports.Add(Consume().Value);

                if (Peek().Type is TokenType.Comma) Consume(); // Consume trailing comma
            }
            
            return new ImportStatementNode(identifier, subImports.ToValueList(), start.To(Peek().Span.End));
        }

        return new ImportStatementNode(identifier, [], start.To(Peek().Span.End));
    }
    
    
    // Expressions
    ExpressionNode ConsumeExpression(bool allowUnparenthesizedBlock = true)
    {
        return ConsumeRange(allowUnparenthesizedBlock); // Start with range and cascade down
    }
    ExpressionNode ConsumePrimary()
    {
        var firstToken = Peek().Type;

        // Ensure Dot is excluded from prefix unary operations
        if (firstToken.IsOperator() && firstToken is not TokenType.Dot)
        {
            return ConsumeUnary();
        }

        switch (firstToken)
        {
            case TokenType.ParenthesisOpen:
                return ConsumeGrouped();
            
            case TokenType.IfKeyword:
                return ConsumeIf();
            case TokenType.ForKeyword:
                return ConsumeFor();
            case TokenType.MatchKeyword:
                return ConsumeMatch();
            case TokenType.ConcurrentKeyword:
                return ConsumeConcurrent();
            case TokenType.SpawnKeyword:
                return ConsumeSpawn();
            case TokenType.Identifier:
                return ConsumeIdentifierExpr();
            case TokenType.InterpolationStart:
                return ConsumeInterpolation();
            case TokenType.BracketOpen:
                return ConsumeLambda();
            case TokenType.CharLiteral
                or TokenType.StringLiteral
                or TokenType.IntLiteral
                or TokenType.FloatLiteral
                or TokenType.BooleanLiteral
                or TokenType.NullLiteral
                or TokenType.SelfLiteral
                or TokenType.ItLiteral
                or TokenType.SquareOpen:
                return ConsumeLiteral();
            default:
                Error($"Unexpected token: {firstToken}", Peek().Span);
                return null!;
        }
    }
    ExpressionNode ConsumePostfix(bool allowUnparenthesizedBlock = true)
    {
        var expr = ConsumePrimary();

        while (true)
        {
            var tokenType = Peek().Type;

            if (tokenType is TokenType.Dot)
            {
                expr = ConsumeMemberAccess(expr);
            }
            else if (tokenType is TokenType.ParenthesisOpen)
            {
                expr = ConsumeCall(expr);
            }
            else if (tokenType is TokenType.BracketOpen && allowUnparenthesizedBlock)
            {
                expr = ConsumeCall(expr);
            }
            else if (tokenType is TokenType.SquareOpen)
            {
                expr = ConsumeIndex(expr);
            }
            else
            {
                break;
            }
        }

        return expr;
    }
    ExpressionNode ConsumeGrouped()
    {
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");
        var expr = ConsumeExpression(); // Cascades back down to the lowest precedence level
        ExpectType(TokenType.ParenthesisClose, "Expected ')'");
        
        return expr; 
    }
    BlockExpressionNode ConsumeBlock()
    {
        var startSpan = Peek().Span;
        
        List<StatementNode> statements = [];
        if (Peek().Type is TokenType.EqualArrow)
        {
            Consume(); // Consume '=>'
            statements.Add(ConsumeStatement());
        }
        else
        {
            ExpectType(TokenType.BracketOpen, "Expected '{'");

            while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
            {
                statements.Add(ConsumeStatement());
            }
            
            ExpectType(TokenType.BracketClose, "Expected '}'");
        }
        
        return new BlockExpressionNode(statements.ToValueList(), startSpan.To(Peek().Span));
    }
    IfExpressionNode ConsumeIf()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.IfKeyword, "Expected 'if'");
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");
        var condition = ConsumeExpression();
        ExpectType(TokenType.ParenthesisClose, "Expected ')'");

        var thenBranch = ConsumeBlock();
        
        List<(ExpressionNode condition, BlockExpressionNode body)> elseIfs = [];
        while (Peek().Type is TokenType.ElseKeyword && Peek(1).Type is TokenType.IfKeyword)
        {
            ExpectType(TokenType.ElseKeyword, "Expected 'else'");
            ExpectType(TokenType.IfKeyword, "Expected 'if'");
            
            ExpectType(TokenType.ParenthesisOpen, "Expected '('");
            var elseIfCondition = ConsumeExpression();
            ExpectType(TokenType.ParenthesisClose, "Expected ')'");
            
            var elseIfBranch = ConsumeBlock();
            
            elseIfs.Add((elseIfCondition, elseIfBranch));
        }
        
        BlockExpressionNode? elseBranch = null;
        if (Peek().Type is TokenType.ElseKeyword)
        {
            ExpectType(TokenType.ElseKeyword, "Expected 'else'");
                
            elseBranch = ConsumeBlock();
        }
        
        return new IfExpressionNode(condition, thenBranch, elseIfs.ToValueList(), elseBranch, startSpan.To(Peek().Span));
    }
    ForExpressionNode ConsumeFor()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.ForKeyword, "Expected 'for'");
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");
        
        var paramStart = Peek().Span;
        var name = ConsumeIdentifier();
        var type = AeroType.Auto;
        if (Peek().Type is TokenType.Colon)
        {
            ExpectType(TokenType.Colon, "Expected ':'");
            type = ConsumeType();
        }
        
        ExpectType(TokenType.InKeyword, "Expected 'in'");
        
        var collection = ConsumeExpression();
        ExpectType(TokenType.ParenthesisClose, "Expected ')'");

        var body = ConsumeBlock();
        
        return new ForExpressionNode(new ParamNode(name, type, paramStart.To(Peek().Span)), collection, body, startSpan.To(Peek().Span));
    }
    SpawnExpressionNode ConsumeSpawn()
    {
        var startSpan = Peek().Span;
        ExpectType(TokenType.SpawnKeyword, "Expected 'spawn'");
        
        var body = ConsumeBlock();
        
        return new SpawnExpressionNode(body, startSpan.To(Peek().Span));
    }
    ConcurrentExpressionNode ConsumeConcurrent()
    {
        var startSpan = Peek().Span;
        ExpectType(TokenType.ConcurrentKeyword, "Expected 'concurrent'");
        
        var body = ConsumeBlock();
        
        return new ConcurrentExpressionNode(body, startSpan.To(Peek().Span));
    }
    MatchExpressionNode ConsumeMatch()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.MatchKeyword, "Expected 'match'");
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");
        var target = ConsumeExpression();
        ExpectType(TokenType.ParenthesisClose, "Expected ')'");

        ExpectType(TokenType.BracketOpen, "Expected '{'");

        List<CaseExpressionNode> cases = [];
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            var patternStart = Peek().Span;
            
            var pattern = ConsumeExpression(false);
            var body = ConsumeBlock();

            if (Peek().Type is TokenType.Comma) Consume(); // Consume ','
            
            cases.Add(new CaseExpressionNode(pattern, body, patternStart.To(Peek().Span)));
        }
        ExpectType(TokenType.BracketClose, "Expected '}'");

        return new MatchExpressionNode(target, cases.ToValueList(), startSpan.To(Peek().Span));
    }
    ExpressionNode ConsumeLiteral()
    {
        var startSpan = Peek().Span;
        
        var token = Peek();
        object? obj = null;

        switch (token.Type)
        {
            case TokenType.CharLiteral:
                if (token.Value.Length != 1) Error("Too many characters", startSpan);
                obj = token.Value.FirstOrDefault();
                break;
            case TokenType.StringLiteral:
                obj = token.Value;
                break;
            case TokenType.IntLiteral:
                if (!TryParseIntLiteral(token.Value, out var i))
                {
                    Error("Int could not be parsed", startSpan);
                }
                obj = i;
                break;
            case TokenType.FloatLiteral:
                if (!float.TryParse(token.Value, out var f))
                {
                    Error("Float could not be parsed", startSpan);
                }
                obj = f;
                break;
            case TokenType.BooleanLiteral:
                obj = token.Value == "true";
                break;
            case TokenType.SelfLiteral or TokenType.ItLiteral or TokenType.NullLiteral:
                obj = token.Value;
                break;
            case TokenType.SquareOpen:
                Consume(); // Consume '['

                List<ExpressionNode> elements = [];
                while (Peek().Type is not TokenType.SquareClose and not TokenType.Eof)
                {
                    elements.Add(ConsumeExpression());
                    if (Peek().Type is TokenType.Comma) Consume(); // Consume ','
                }
                ExpectType(TokenType.SquareClose, "Expected ']'");

                return new ArrayLiteralExpressionNode(elements.ToValueList(), startSpan.To(Peek().Span));
        }
        
        if (obj is null) Error("Literal could not be parsed", startSpan);
        else Consume(); // Consume the literal token
        
        return new LiteralExpressionNode(obj, token.Type, startSpan.To(Peek().Span));
    }
    IdentifierExpressionNode ConsumeIdentifierExpr()
    {
        var token = ExpectType(TokenType.Identifier, "LogicalNot an identifier");
        return new IdentifierExpressionNode(token.Value, token.Span);
    }
    MemberAccessExpressionNode ConsumeMemberAccess(ExpressionNode source)
    {
        var startSpan = source.Span;
        ExpectType(TokenType.Dot, "Expected '.'");
        var member = ConsumeIdentifierExpr();

        return new MemberAccessExpressionNode(source, member, startSpan.To(Peek().Span));
    }
    CallExpressionNode ConsumeCall(ExpressionNode target)
    {
        var startSpan = target.Span;
        List<ExpressionNode> parameters = [];
        
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            Consume(); // Consume '('

            while (Peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
            {
                parameters.Add(ConsumeExpression());

                if (Peek().Type is TokenType.Comma)
                {
                    Consume();
                }
            }

            ExpectType(TokenType.ParenthesisClose, "Expected ')'");
        }

        // Kotlin style trailing lambda parsing
        if (Peek().Type is TokenType.BracketOpen)
        {
            parameters.Add(ConsumeLambda());
        }

        return new CallExpressionNode(target, parameters.ToValueList(), startSpan.To(Peek().Span));
    }
    IndexExpressionNode ConsumeIndex(ExpressionNode target)
    {
        var startSpan = target.Span;
        
        ExpectType(TokenType.SquareOpen, "Expected '['");
        var index = ConsumeExpression();
        ExpectType(TokenType.SquareClose, "Expected ']'");
        
        return new IndexExpressionNode(target, index, startSpan.To(Peek().Span));
    }
    ExpressionNode ConsumeRange(bool allowUnparenthesizedBlock = true)
    {
        // 1. Handle Prefix Range (..b) or Full Range (..)
        if (IsRangeToken())
        {
            var startSpan = Peek().Span;
            Consume(); // Consume '..'

            ExpressionNode? right = null;
            if (CanStartExpression())
            {
                right = ConsumeBinary(allowUnparenthesizedBlock);
            }

            return new RangeExpressionNode(null, right, startSpan.To(Peek().Span));
        }

        // 2. Parse the left-hand expression
        var left = ConsumeBinary(allowUnparenthesizedBlock);

        // 3. Handle Binary Range (a..b) or Postfix Range (a..)
        if (IsRangeToken())
        {
            var startSpan = left.Span;
            Consume(); // Consume '..'

            ExpressionNode? right = null;
            if (CanStartExpression())
            {
                right = ConsumeBinary(allowUnparenthesizedBlock);
            }

            return new RangeExpressionNode(left, right, startSpan.To(Peek().Span));
        }

        return left;
    }
    ExpressionNode ConsumeBinary(bool allowUnparenthesizedBlock = true)
    {
        var left = ConsumePostfix(allowUnparenthesizedBlock);

        while (Peek().Type.IsOperator() && Peek().Type is not TokenType.Dot && !IsRangeToken())
        {
            var startSpan = left.Span;
            var op = ConsumeOperator();
            var right = ConsumePostfix(allowUnparenthesizedBlock);

            left = new BinaryExpressionNode(left, op, right, startSpan.To(Peek().Span));
        }

        return left;
    }
    UnaryExpressionNode ConsumeUnary()
    {
        var startSpan = Peek().Span;

        bool isPostFix = !Peek().Type.IsOperator();
        Operator op;
        ExpressionNode target;
        if (isPostFix)
        {
            target = ConsumeExpression();
            op = ConsumeOperator();
        }
        else
        {
            op = ConsumeOperator();
            target = ConsumeExpression();
        }

        return new UnaryExpressionNode(op, target, isPostFix, startSpan.To(Peek().Span));
    }
    LambdaExpressionNode ConsumeLambda()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.BracketOpen, "Expected '{'");

        List<ParamNode> parameters = [];
        if (Peek().Type is TokenType.Identifier) // If there is at least one parameter, then parse the lambda
        {
            while (Peek().Type is not TokenType.ArrowSymbol and not TokenType.Eof)
            {
                var paramStart = Peek().Span;
            
                var name = ConsumeIdentifier();
                var type = AeroType.Auto;
                if (Peek().Type is TokenType.Colon)
                {
                    Consume(); // Consume ':'
                    type = ConsumeType();
                }
            
                parameters.Add(new ParamNode(name, type, paramStart.To(Peek().Span)));
            }
            ExpectType(TokenType.ArrowSymbol, "Expected '->'");
        }
        
        List<StatementNode> statements = [];
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            statements.Add(ConsumeStatement());
        }
        ExpectType(TokenType.BracketClose, "Expected '}'");
        var block = new BlockExpressionNode(statements.ToValueList(), startSpan.To(Peek().Span));
        
        return new LambdaExpressionNode(parameters.ToValueList(), block, startSpan.To(Peek().Span));
    }
    StringInterpolationExpressionNode ConsumeInterpolation()
    {
        var startSpan = Peek().Span;

        ExpectType(TokenType.InterpolationStart, "Expected '$'");
        List<ExpressionNode> parts = [];

        while (Peek().Type is not TokenType.InterpolationEnd and not TokenType.Eof)
        {
            // 1. Raw string segment fragment
            if (Peek().Type is TokenType.StringLiteral)
            {
                parts.Add(ConsumeLiteral());
            }
            // 2. Embedded expression within braces: ${ expr } or { expr }
            else if (Peek().Type is TokenType.BracketOpen)
            {
                Consume(); // Consume '{'
                parts.Add(ConsumeExpression());
                ExpectType(TokenType.BracketClose, "Expected '}' after interpolated expression");
            }
            // 3. Direct inline expression: $identifier
            else
            {
                parts.Add(ConsumeExpression());
            }
        }

        if (Peek().Type is TokenType.InterpolationEnd)
        {
            Consume(); // Consume closing string delimiter / token
        }

        return new StringInterpolationExpressionNode(parts.ToValueList(), startSpan.To(Peek().Span));
    }
    
    
    // Node Helpers
    bool IsRangeToken() => Peek().Type is TokenType.RangeSymbol;
    bool CanStartExpression(int offset = 0)
    {
        return Peek(offset).Type is not (TokenType.SquareClose 
            or TokenType.ParenthesisClose 
            or TokenType.BracketClose 
            or TokenType.Comma 
            or TokenType.Semicolon 
            or TokenType.Eof);
    }
    Operator ConsumeOperator()
    {
        var opToken = Peek();
        var op = Operator.Assign;
        if (Peek().Type.IsOperator()) op = Consume().Type.ToOperator();
        else Error($"'{opToken.Value}' is not an operator", Consume().Span);

        return op;
    }
    string ConsumeIdentifier()
    {
        // Make sure that the identifier is not nothing
        var first = ExpectType(TokenType.Identifier, "Identifier not found").Value;
        
        string name = first;
        if (Peek().Type is TokenType.Dot) name += Consume().Value;
        
        while (Peek().Type is TokenType.Identifier)
        {
            name += Consume().Value;

            // Consume the dot between two identifiers and add it
            if (Peek().Type is TokenType.Dot && Peek(1).Type is TokenType.Identifier)
            {
                name += Consume().Value;
            }
        }

        return name;
    }
    AccessMod? ConsumeAccessMod()
    {
        AccessMod? mod = null;
        
        if (Peek().Type is TokenType.AccessModifierKind)
        {
            mod = Consume().Value switch
            {
                "public" => AccessMod.Public,
                "internal" => AccessMod.Internal,
                "protected" => AccessMod.Protected,
                _ => AccessMod.Private
            };
        }
        
        return mod;
    }
    MemberMod ConsumeMemberMod()
    {
        var mod = MemberMod.None;
        
        while (Peek().Type is TokenType.MemberModifierKind)
        {
            mod |= Consume().Value switch
            {
                "static" => MemberMod.Static,
                "weak" => MemberMod.Weak,
                "partial" => MemberMod.Partial,
                "unsage" => MemberMod.Unsafe,
                _ => MemberMod.None
            };
        }

        return mod;
    }
    InheritanceMod ConsumeInheritance()
    {
        var mod = InheritanceMod.None;

        if (Peek().Type is TokenType.InheritanceModifierKind)
        {
            mod = Consume().Value switch
            {
                "virtual" => InheritanceMod.Virtual,
                "abstract" => InheritanceMod.Abstract,
                "sealed" => InheritanceMod.Sealed,
                "impl" => InheritanceMod.Implements,
                _ => InheritanceMod.None
            };
        }

        return mod;
    }
    VariableKind? ConsumeVarKind()
    {
        VariableKind? mod = null;
        
        if (Peek().Type is TokenType.VariableKind)
        {
            mod = Consume().Value switch
            {
                "var" => VariableKind.Var,
                "const" => VariableKind.Const,
                _ => VariableKind.Val

            };
        }
        
        return mod;
    }
    AeroType ConsumeType()
    {
        AeroType baseType;
        
        bool isRef = Peek().Type is TokenType.RefKeyword;
        if (isRef) Consume();

        // A lambda type
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            Consume(); // Consume '('

            List<TypeParam> parameters = [];
            while (Peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
            {
                parameters.Add(ConsumeTypeParam());
                if (Peek().Type is TokenType.Comma) Consume(); // Consume ','
            }
            ExpectType(TokenType.ParenthesisClose, "Expected ')'");

            bool isLambdaNullable = Peek().Type is TokenType.Nullable;
            if (isLambdaNullable) Consume();
            
            AeroType returnType = AeroType.Void;
            if (Peek().Type is TokenType.EqualArrow)
            {
                Consume(); // Consume '=>'
                returnType = ConsumeType();
            }
            
            baseType = new LambdaType(parameters.ToValueList(), returnType, isRef, isLambdaNullable);
        }
        // A scalar / generic type
        else
        {
            if (Peek().Type is not TokenType.Identifier) return AeroType.Error;
            var name = ConsumeIdentifier();
        
            // A generic type
            List<GenericParameterType>? generics = null;
            if (Peek().Type is TokenType.LessThan)
            {
                Consume(); // Consume '<'
                generics = [];
            
                while (Peek().Type is not TokenType.GreaterThan and not TokenType.Eof) // Make sure trailing commas are handled correctly and do not try to
                {
                    // ref G? : String
                
                    bool isParamRef = Peek().Type is TokenType.RefKeyword;
                    if (isParamRef) Consume();

                    var paramName = ConsumeIdentifier();
                
                    bool isParamNullable = Peek().Type is TokenType.Nullable;
                    if (isParamNullable) Consume();

                    AeroType? constraint = null;
                    if (Peek().Type is TokenType.Colon)
                    {
                        Consume(); // Consume ':'
                        constraint = ConsumeType();
                    }
                
                    if (Peek().Type is TokenType.Comma)
                    {
                        Consume(); // Consume ','
                    }

                    generics.Add(new GenericParameterType(paramName, constraint, isParamRef, isParamNullable));
                }
                ExpectType(TokenType.GreaterThan, "Expected '>'");
            }
        
            bool isNullable = Peek().Type is TokenType.Nullable;
            if (isNullable) Consume();
        
            baseType = new ScalarType(
                Name: name, 
                IsRef: false, 
                IsNullable: isNullable
            );

            // If it is a generic type, replace the baseType with it
            if (generics is not null) baseType = new GenericType(baseType, generics.ToValueList());
        }
        
        while (Peek().Type is TokenType.SquareOpen)
        {
            Consume(); // Consume '['
            ExpectType(TokenType.SquareClose, "Expected ']'");
            
            bool isArrayNullable = Peek().Type is TokenType.Nullable;
            if (isArrayNullable) Consume();
            
            baseType = new ArrayType(
                ElementType: baseType,
                IsRef: false, 
                IsNullable: isArrayNullable
            );
        }
        
        return baseType with { IsRef = isRef };
    }
    TypeParam ConsumeTypeParam()
    {
        var name = ConsumeIdentifier();
        ExpectType(TokenType.Colon, "Expected ':'");
        var type = ConsumeType();

        return new TypeParam(name, type);
    }
    ValueList<ParamNode> ConsumeParameterDecl()
    {
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");

        var result = new List<ParamNode>();
        while (Peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
        {
            var paramStart = Peek().Span;

            var varKind = ConsumeVarKind() ?? VariableKind.Val;
            var name = ConsumeIdentifier();

            ExpectType(TokenType.Colon, "Expected ':'");
            
            var type = ConsumeType();
            ExpressionNode? init = null;

            if (Peek().Type is TokenType.Equality)
            {
                Consume(); // Consume '='

                init = ConsumeExpression();
            }
            
            result.Add(new ParamNode(name, type, paramStart.To(Peek().Span), init, varKind));
            
            if (Peek().Type is TokenType.Comma)
            {
                Consume(); // Consume ','
                
                // Allow trailing comma: Foo(a, b,)
                if (Peek().Type is TokenType.ParenthesisClose)
                {
                    break;
                }
            }
        }
        
        Consume(); // Consume ')'
        
        return result.ToValueList();
    }
    ValueList<AeroType> ConsumeImplementations()
    {
        List<AeroType> results = [];
        if (Peek().Type is TokenType.Colon)
        {
            Consume(); // Consume ':'
            
            while (Peek().Type is not TokenType.Eof and not TokenType.Semicolon and not TokenType.BracketOpen)
            {
                results.Add(ConsumeType());

                if (Peek().Type is TokenType.Comma) Consume(); // Consume ','
            }
        }

        return results.ToValueList();
    }
    ValueList<GenericParamNode> ConsumeGenericDecls()
    {
        List<GenericParamNode> generics = [];
        if (Peek().Type is TokenType.LessThan)
        {
            Consume(); // Consume '<'
            
            while (Peek().Type is not TokenType.GreaterThan and not TokenType.Eof)
            {
                var startSpan = Peek().Span;

                var name = ConsumeIdentifier();
                AeroType typeConstraint = AeroType.Void;

                if (Peek().Type is TokenType.Colon)
                {
                    Consume(); // Consume ':'
                    
                    typeConstraint = ConsumeType();
                }
                
                generics.Add(new(name, typeConstraint, startSpan.To(Peek().Span)));

                // Consumes
                if (Peek().Type is TokenType.Comma && Peek(1).Type is not TokenType.GreaterThan)
                {
                    Consume(); // Consume commas
                }
            }

            ExpectType(TokenType.GreaterThan, "Expected '>'");
        }

        return generics.ToValueList();
    }
    ValueList<AnnotationNode> ConsumeAnnotations()
    {
        List<AnnotationNode> annotations = [];
        while (Peek().Type is TokenType.At and not TokenType.Eof)
        {
            annotations.Add(ConsumeAnnotation());
        }

        return annotations.ToValueList();
    }
    bool IsStatementTerminator()
    {
        // Explicit terminators or scope closers
        if (Peek().Type is TokenType.Semicolon or TokenType.Eof)
            return true;

        // Check if a line break occurred between the previous consumed token and current token
        return Peek().Span.Start.Line > Peek(-1).Span.End.Line;
    }
    void ConsumeStatementTerminator()
    {
        if (IsStatementTerminator())
        {
            if (Peek().Type is TokenType.Semicolon) Consume();
        }
    }
    bool TryParseIntLiteral(string text, out int result)
    {
        result = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // 1. Remove digit separators '_'
        string clean = text.Replace("_", "");

        try
        {
            // 2. Parse Hexadecimal (0x / 0X) -> Base 16
            if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                result = Convert.ToInt32(clean[2..], 16);
                return true;
            }

            // 3. Parse Binary (0b / 0B) -> Base 2
            if (clean.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                result = Convert.ToInt32(clean[2..], 2);
                return true;
            }

            // 4. Parse Decimal -> Base 10
            return int.TryParse(clean, out result);
        }
        catch
        {
            return false;
        }
    }
    
    // Helper methods
    void ExpectInstance(string[] instanceNames, string errorMessage, SourceSpan? location = null) => Expect(t => t.Type is TokenType.InstanceKind && instanceNames.Contains(t.Value), errorMessage, location);
    Token ExpectType(TokenType expectedType, string errorMessage, SourceSpan? location = null) => Expect(t => t.Type == expectedType, errorMessage, location);
    protected override void Synchronize()
    {
        // Advance past the token that caused the error
        if (Peek().Type != TokenType.Eof)
        {
            Consume();
        }

        while (Peek().Type != TokenType.Eof)
        {
            // Stop if we hit a statement terminator or scope boundary
            if (CanStartExpression(-1))
            {
                return;
            }

            // Stop if we land on a major statement/declaration keyword start boundary
            switch (Peek().Type)
            {
                case TokenType.IfKeyword:
                case TokenType.ForKeyword:
                case TokenType.MatchKeyword:
                case TokenType.BracketClose:
                    return;
            }

            Consume();
        }
    }
}