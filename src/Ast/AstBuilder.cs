using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.Utility;

namespace Luft.Ast;

public sealed class AstBuilder : SafeIterator<Token>
{
    private static readonly TokenType[] ExcludedTypes = [TokenType.Whitespace, TokenType.Comment, TokenType.Unknown];

    public AstBuilder()
    {
        Denied = token => ExcludedTypes.Contains(token.Type);
    }

    public FileNode BuildAst(Token[] rawTokens)
    {
        Start(rawTokens);
        
        return PopFile();
    }
    
    
    // Special
    FileNode PopFile()
    {
        var imports = new List<ImportStatementNode>();
        var modules = new List<ModuleDeclarationNode>();
        var globals = new List<DeclarationNode>();

        while (Peek().Type is not TokenType.Eof)
        {
            if (Peek().Type is TokenType.ImportKeyword or TokenType.FromKeyword)
            {
                imports.Add(PopImport());
            }
            else if (Peek().Type is TokenType.ModuleKeyword)
            {
                modules.Add(PopModule());
            }
            else
            {
                globals.Add(PopDecl());
            }
        }
        
        if (globals.Any()) modules.Add(new ModuleDeclarationNode("", globals.ToArray(), Items[0].Span.To(Items[^1].Span)));
        
        return new FileNode(modules.ToArray(), imports.ToArray(), Peek().Span);
    }
    AnnotationStatementNode PopAnnotation()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.At,"Expected '@' to start annotation.");
        
        var name = ExpectType(TokenType.Identifier, "Identifier not found.").Value;
        
        // Handle parameters if passed
        var parameters = new List<ExpressionNode>();
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            Pop(); // Pop '('
            while (Peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
            {
                parameters.Add(PopExpression());
                
                if (Peek().Type is TokenType.Comma)
                {
                    Pop(); // Pop ','
                    
                    // Allow trailing comma: @Foo(a, b,)
                    if (Peek().Type is TokenType.ParenthesisClose)
                    {
                        break;
                    }
                }
                else if (Peek().Type is not TokenType.ParenthesisClose)
                {
                    Error("Expected ',' or ')' after parameter.", Peek().Span);
                    Pop();
                    break; 
                }
            }

            ExpectType(TokenType.ParenthesisClose, "Expected ')' to close annotation arguments.");
        }

        return new AnnotationStatementNode(name, parameters.ToValueList(), startSpan.To(Peek(-1).Span.End));
    }
    
    
    // Declarations
    DeclarationNode PopDecl()
    {
        var annotations = PopAnnotations();
        var accessMod = PopAccessMod();
        var memberMod = PopMemberMod();
        var inheritance = PopInheritance();
        
        return Peek().Value switch
        {
            "struct" => PopStruct(annotations, accessMod, memberMod, inheritance),
            "record" => PopRecord(annotations, accessMod, memberMod, inheritance),
            "class" => PopClass(annotations, accessMod, memberMod, inheritance),
            "trait" => PopTrait(annotations, accessMod, inheritance),
            "enum" => PopEnum(annotations, accessMod),
            "annotation" => PopAnnotationDecl(annotations, accessMod, memberMod),
            "fun" => PopFunction(annotations, accessMod, memberMod, inheritance),
            "extension" => PopExtension(annotations, accessMod, memberMod, inheritance),
            "extensions" => PopExtensionBlock(accessMod),
            "constructor" => PopConstructor(annotations, accessMod),
            "destructor" => PopDestructor(annotations),
            _ => Peek().Type is TokenType.VariableKind ? PopVariableDecl(annotations, accessMod, memberMod, inheritance) : PopProperty(annotations, accessMod, memberMod, inheritance)
        };
    }
    ModuleDeclarationNode PopModule()
    {
        ExpectType(TokenType.ModuleKeyword, "Use the 'module' keyword to declare a module.");
        var identifier= PopIdentifier();
        
        var decls = new List<DeclarationNode>();
        if (Peek().Type is TokenType.BracketOpen)
        {
            Pop(); // Pop '{'
            
            while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
            {
                decls.Add(PopDecl());
            }

            ExpectType(TokenType.BracketClose, "Expected '}'");
        }
        else
        {
            PopStatementTerminator();
            
            while (Peek().Type is not TokenType.Eof)
            {
                decls.Add(PopDecl());
            }
        }
        
        return new ModuleDeclarationNode(identifier, decls.ToArray(), Peek().Span);
    }
    FunctionDeclarationNode PopFunction(ValueList<AnnotationStatementNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.FunctionDefault;

        if (Peek().Type is TokenType.InstanceKind && Peek().Value == "extension") Pop(); // Pop 'extension' 
        ExpectInstance(["fun"], "Expected 'fun' keyword");
        
        var name = PopIdentifier();
        var generics = PopGenericDecls();
        var parameters = PopParameterDecl();

        var returning = AeroType.Void;
        if (Peek().Type is TokenType.ArrowSymbol)
        {
            Pop(); // Pop '->'
            returning = PopType();
        }

        BlockExpressionNode? body = null;
        if (Peek().Type is TokenType.BracketOpen or TokenType.EqualArrow)
        {
            body = PopBlock();
        }
        else if (IsStatementTerminator())
        {
            PopStatementTerminator();
        }
        
        return new FunctionDeclarationNode(annotations.OrNew(), access, inheritance, memberMod, returning, name, generics.ToValueList(), parameters, body, startSpan.To(Peek().Span.End));
    }
    ExtensionDeclarationNode PopExtension(ValueList<AnnotationStatementNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        string targetType;
        DeclarationNode decl;
        var kind = Peek(1);
        if (kind.Type is TokenType.InstanceKind && kind.Value == "fun") // Peek() is 'extension', so check the next one
        {
            var node = PopFunction(annotations, accessMod, memberMod, inheritance);
            decl = node;
            targetType = node.Name.FirstIdentifier();
        }
        else
        {
            var node = PopProperty(annotations, accessMod, memberMod, inheritance);
            decl = node;
            targetType = node.Name.FirstIdentifier();
        }
        
        return new ExtensionDeclarationNode(decl, targetType.ToType(), decl.Span);
    }
    ExtensionBlockDeclarationNode PopExtensionBlock(AccessMod? accessMod)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.ExtensionDefault;
        
        ExpectInstance(["extensions"], "Expected 'extensions'");
        
        var target = PopType();
        
        ExpectType(TokenType.BracketOpen, "Expected '{'");
        List<ExtensionDeclarationNode> extensions = [];
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            var declStart = Peek().Span;
            var decl = PopDecl();
            extensions.Add(new ExtensionDeclarationNode(decl, target, declStart.To(Peek().Span)));
        }
        ExpectType(TokenType.BracketClose, "Expected '}'");
        
        return new ExtensionBlockDeclarationNode(access, target, extensions.ToValueList(), startSpan.To(Peek().Span));
    }
    StructDeclarationNode PopStruct(ValueList<AnnotationStatementNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.StructDeclDefault;
        
        // Make sure the struct keyword was used
        ExpectInstance(["struct"], "Expected 'struct'");

        var name = PopIdentifier();
        
        List<DeclarationNode> decls = [];
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            decls.Add(PopPrimaryConstructor());
        }

        var implementations = PopImplementations();

        ExpectType(TokenType.BracketOpen, "Expect '{'");
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            decls.Add(PopDecl());
        }
        ExpectType(TokenType.BracketClose, "Expect '}'");
        
        return new StructDeclarationNode(annotations.OrNew(), access, inheritance, memberMod, name, decls.ToValueList(), implementations, startSpan.To(Peek().Span));
    }
    RecordDeclarationNode PopRecord(ValueList<AnnotationStatementNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.RecordDeclDefault;

        // Make sure the record keyword was used
        ExpectInstance(["record"], "Expected 'record'");
        
        var name = PopIdentifier();
        
        var generics = PopGenericDecls();
        
        List<DeclarationNode> decls = [];
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            decls.Add(PopPrimaryConstructor());
        }

        var implementations = PopImplementations();
        
        ExpectType(TokenType.BracketOpen, "Expected '{'");
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            decls.Add(PopDecl());
        }
        ExpectType(TokenType.BracketClose, "Expected '}'");
        
        return new RecordDeclarationNode(annotations.OrNew(), access, inheritance, memberMod, name, generics, decls.ToValueList(), implementations, startSpan.To(Peek().Span));
    }
    AnnotationDeclarationNode PopAnnotationDecl(ValueList<AnnotationStatementNode>? annotations, AccessMod? accessMod, MemberMod memberMod)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.AnnotationDeclDefault;

        // Make sure the annotation keyword was used
        ExpectInstance(["annotation"], "Expected 'annotation'");
        
        var name = PopIdentifier();
        
        var generics = PopGenericDecls();
        
        List<DeclarationNode> decls = [];
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            decls.Add(PopPrimaryConstructor());
        }
        
        return new AnnotationDeclarationNode(annotations.OrNew(), access, memberMod, name, generics, decls.ToValueList(), startSpan.To(Peek().Span));
    }
    ClassDeclarationNode PopClass(ValueList<AnnotationStatementNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.ClassDeclDefault;

        // Make sure the class keyword was used
        ExpectInstance(["class"], "Expected 'class'");
        
        var name = PopIdentifier();
        var generics = PopGenericDecls();
        
        List<DeclarationNode> decls = [];
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            decls.AddRange(PopPrimaryConstructor());
        }

        var implementations = PopImplementations();
        
        ExpectType(TokenType.BracketOpen, "Expect '{'");
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            decls.Add(PopDecl());
        }
        ExpectType(TokenType.BracketClose, "Expect '}'");
        
        return new ClassDeclarationNode(annotations.OrNew(), access, inheritance, memberMod, name, generics, decls.ToValueList(), implementations, startSpan.To(Peek().Span));
    }
    TraitDeclarationNode PopTrait(ValueList<AnnotationStatementNode>? annotations, AccessMod? accessMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.TraitDeclDefault;

        // Make sure the class keyword was used
        ExpectInstance(["trait"], "Expected 'trait'");
        
        var name = PopIdentifier();
        var generics = PopGenericDecls();
        var implementations = PopImplementations();

        ExpectType(TokenType.BracketOpen, "Expected '{'");
        List<DeclarationNode> decls = [];
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            decls.Add(PopDecl());
        }
        ExpectType(TokenType.BracketClose, "Expected '}'");
        
        return new TraitDeclarationNode(annotations.OrNew(), access, inheritance, name, generics, decls.ToValueList(), implementations, startSpan.To(Peek().Span));
    }
    EnumDeclarationNode PopEnum(ValueList<AnnotationStatementNode>? annotations, AccessMod? accessMod)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.EnumDeclDefault;
        
        // Make sure the enum keyword was used and check if it's an enum class
        ExpectInstance(["enum"], "Expected 'enum'");
        bool isEnumClass = Peek().Type is TokenType.InstanceKind && Peek().Value == "class";
        if (isEnumClass) Pop(); // Pop 'class'

        var name = PopIdentifier();

        ValueList<ParamNode>? memberValues = null;
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            memberValues = PopParameterDecl();
        }
        
        AeroType? memberType = null;
        if (Peek().Type is TokenType.Colon)
        {
            Pop(); // Pop ':'
            memberType = PopType();
        }

        ExpectType(TokenType.BracketOpen, "Expected '{'");
        List<EnumMemberNode> members = [];
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            var memberStartSpan = Peek().Span;
            
            var memberName = PopIdentifier();
            ExpressionNode? memberValue = null;
            if (Peek().Type is TokenType.Assign)
            {
                Pop(); // Pop '='
                memberValue = PopExpression();
            }
            members.Add(new EnumMemberNode(memberName, memberValue, memberStartSpan.To(Peek().Span)));
            
            if (Peek().Type is TokenType.Comma) Pop(); // Pop ','
        }
        ExpectType(TokenType.BracketClose, "Expected '}'");
        
        return new EnumDeclarationNode(annotations.OrNew(), access, name, isEnumClass, memberType, memberValues, members.ToValueList(),  startSpan.To(Peek().Span));
    }
    PropertyDeclarationNode PopProperty(ValueList<AnnotationStatementNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.PropertyDefault;

        if (Peek().Type is TokenType.InstanceKind && Peek().Value == "extension") Pop(); // Pop 'extension'
        
        var name = PopIdentifier();

        ExpectType(TokenType.Colon, "Expected ':'");

        var type = PopType();

        ExpectType(TokenType.BracketOpen, "Expected '{'");

        PropertyAccessorNode? getter = null;
        PropertyAccessorNode? setter = null;
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            if (Peek().Type is TokenType.GetKeyword or TokenType.SetKeyword or TokenType.InitKeyword)
            {
                var accessorStart = Peek().Span;
                var accessorAccess = PopAccessMod() ?? AccessMod.Public;
                
                var kind = Peek().Value.GetAccessorKind() ?? PropertyAccessorKind.Get;
                Pop(); // Pop 'get|set|init'
                
                if (kind is PropertyAccessorKind.Get && getter != null)
                {
                    Error("You can only declare one getter for a property.", Peek().Span);
                    continue;
                }
                if (kind is PropertyAccessorKind.Set or PropertyAccessorKind.Init && setter != null)
                {
                    Error("You can only declare one setter for a property.", Peek().Span);
                    continue;
                }
                
                BlockExpressionNode? accessorBlock = null;
                if (!IsStatementTerminator()) accessorBlock = PopBlock();
                else PopStatementTerminator();
                
                var accessor = new PropertyAccessorNode(accessorAccess, accessorBlock, kind, accessorStart);

                if (kind is not PropertyAccessorKind.Get) setter = accessor;
                else getter = accessor;
            }
            else
            {
                Error("You can only declare Property accessors here.", Peek().Span);
                Pop(); // Pop the unknown token
            }
        }
        
        ExpectType(TokenType.BracketClose, "Expected '}'");
        
        ExpressionNode? init = null;
        if (Peek().Type is TokenType.Assign)
        {
            Pop(); // Pop '='
            init = PopExpression();
        }
        
        PopStatementTerminator();
        return new PropertyDeclarationNode(annotations.OrNew(), access, inheritance, memberMod, type, name, getter, setter, init, startSpan.To(Peek().Span));
    }
    FieldDeclarationNode PopVariableDecl(ValueList<AnnotationStatementNode>? annotations, AccessMod? accessMod, MemberMod memberMod, InheritanceMod inheritance)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.VariableDefault;
        
        var varKind = PopVarKind() ?? VariableKind.Val;
        var name = PopIdentifier();

        var type = AeroType.Auto;
        if (Peek().Type is TokenType.Colon)
        {
            Pop(); // Pop ':'
            type = PopType();
        }

        ExpressionNode? init = null;
        if (Peek().Type is TokenType.Assign)
        {
            Pop(); // Pop '='
            init = PopExpression();
        }
        
        PopStatementTerminator();
        return new FieldDeclarationNode(annotations.OrNew(), access, inheritance, memberMod, varKind, type, name, init, startSpan.To(Peek().Span));
    }
    PrimaryConstructorDeclarationNode PopPrimaryConstructor()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");

        var variables = new List<FieldDeclarationNode>();
        while (Peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
        {
            var paramStart = Peek().Span;
            
            var varKind = PopVarKind() ?? VariableKind.Val;
            var name = PopIdentifier();

            ExpectType(TokenType.Colon, "Expected ':'");
            
            var type = PopType();
            
            ExpressionNode? init = null;
            if (Peek().Type is TokenType.Equality)
            {
                Pop(); // Pop '='
                init = PopExpression();
            }
            
            variables.Add(new FieldDeclarationNode([], AccessMod.Private, InheritanceMod.None, MemberMod.None, varKind, type, name, init, paramStart.To(Peek().Span)));
            
            if (Peek().Type is TokenType.Comma) Pop(); // Pop ','
        }

        ExpectType(TokenType.ParenthesisClose, "Expected ')'");

        var endSpan = startSpan.To(Peek().Span);
        return new PrimaryConstructorDeclarationNode(variables.ToValueList(), endSpan);
    }
    ConstructorDeclarationNode PopConstructor(ValueList<AnnotationStatementNode>? annotations, AccessMod? accessMod)
    {
        var startSpan = Peek().Span;
        var access = accessMod ?? AccessModExtensions.ConstructorDefault;

        ExpectInstance(["constructor"], "Expected 'constructor' keyword");
        
        var name = PopIdentifier();
        var parameters = PopParameterDecl();

        BlockExpressionNode body = PopBlock();
        
        return new ConstructorDeclarationNode(annotations.OrNew(), access, name, parameters, body, startSpan.To(Peek().Span.End));
    }
    DestructorDeclarationNode PopDestructor(ValueList<AnnotationStatementNode>? annotations)
    {
        var startSpan = Peek().Span;

        ExpectInstance(["destructor"], "Expected 'destructor' keyword");
        
        var name = PopIdentifier();
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");
        ExpectType(TokenType.ParenthesisClose, "Expected ')'");

        BlockExpressionNode body = PopBlock();
        
        return new DestructorDeclarationNode(annotations.OrNew(), name, body, startSpan.To(Peek().Span.End));
    }
    
    
    // Statements
    StatementNode PopStatement()
    {
        var type = Peek().Type;
        switch (type)
        {
            case TokenType.VariableKind:
                return PopVariable();
            case TokenType.ReturnKeyword:
                return PopReturn();
            case TokenType.WhileKeyword:
                return PopWhile();
            case TokenType.BreakKeyword or TokenType.ContinueKeyword:
                return PopKeyword();
        }
        
        return PopExpressionStatement();
    }
    VariableStatementNode PopVariable()
    {
        var startSpan = Peek().Span;
        
        var varKindNull = PopVarKind();
        if (varKindNull is null) Error("You have to specify the variable declaration kind ('const', 'val', 'var')", startSpan);
        var varKind = varKindNull ?? VariableKind.Val;
        
        var name = PopIdentifier();
        var type = AeroType.Auto;
        if (Peek().Type is TokenType.Colon)
        {
            Pop(); // Pop ':'
            type = PopType();
        }
        
        ExpressionNode? init = null;
        if (Peek().Type is TokenType.Assign)
        {
            Pop(); // Pop '='
            init = PopExpression();
        }
        
        PopStatementTerminator();
        
        return new VariableStatementNode(varKind, type, name, init, startSpan.To(Peek().Span));
    }
    ReturnStatementNode PopReturn()
    {
        var startSpan = Peek().Span;

        ExpectType(TokenType.ReturnKeyword, "Expected 'return'");

        ExpressionNode? val = null;
        if (!IsStatementTerminator()) val = PopExpression();
        
        PopStatementTerminator();
        
        return new ReturnStatementNode(val, startSpan.To(Peek().Span));
    }
    StatementNode PopKeyword()
    {
        var span = Peek().Span;
        StatementNode statement = Peek().Type switch
        {
            TokenType.BreakKeyword => new BreakStatementNode(span),
            TokenType.ContinueKeyword => new ContinueStatementNode(span),
            _ => new EmptyStatementNode(span)
        };

        if (statement is not EmptyStatementNode) Pop(); // Pop the keyword

        PopStatementTerminator();
        
        return statement;
    }
    WhileStatementNode PopWhile()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.WhileKeyword, "Expected 'while'");
        
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");
        var condition = PopExpression();
        ExpectType(TokenType.ParenthesisClose, "Expected ')'");
        
        var body = PopBlock();
        
        PopStatementTerminator();
        
        return new WhileStatementNode(condition, body, startSpan.To(Peek().Span));
    }
    StatementNode PopExpressionStatement()
    {
        var startSpan = Peek().Span;
        
        var expression = PopExpression();
        PopStatementTerminator();
        
        if (expression is BinaryExpressionNode bin && bin.Operator.IsAssignment())
        {
            return new AssignmentStatementNode(bin.Left, bin.Operator, bin.Right, bin.Span);
        }
        
        return new ExpressionStatementNode(expression, startSpan.To(Peek().Span));
    }
    ImportStatementNode PopImport()
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
            ExpectType(TokenType.ImportKeyword, "PopImport keyword not found.");
        }

        var identifier = PopIdentifier();
        
        if (isFrom)
        {
            ExpectType(TokenType.ImportKeyword, "Import keyword not found.");

            List<string> subImports = [];
            while (Peek().Type is TokenType.Identifier and not TokenType.Eof)
            {
                subImports.Add(Pop().Value);

                if (Peek().Type is TokenType.Comma) Pop(); // Pop trailing comma
            }
            
            PopStatementTerminator();
            return new ImportStatementNode(identifier, subImports.ToValueList(), start.To(Peek().Span.End));
        }
        
        PopStatementTerminator();
        return new ImportStatementNode(identifier, [], start.To(Peek().Span.End));
    }
    
    
    // Expressions
    ExpressionNode PopExpression(bool allowUnparenthesizedBlock = true)
    {
        return PopRange(allowUnparenthesizedBlock); // Start with range and cascade down
    }
    ExpressionNode PopPrimary()
    {
        var firstToken = Peek().Type;

        // Ensure Dot is excluded from prefix unary operations
        if (firstToken.IsOperator() && firstToken is not TokenType.Dot)
        {
            return PopUnary();
        }

        switch (firstToken)
        {
            case TokenType.ParenthesisOpen:
                return PopScoped();
            
            case TokenType.IfKeyword:
                return PopIf();
            case TokenType.ForKeyword:
                return PopFor();
            case TokenType.MatchKeyword:
                return PopMatch();
            case TokenType.ConcurrentKeyword:
                return PopConcurrent();
            case TokenType.SpawnKeyword:
                return PopSpawn();
            case TokenType.Identifier:
                return PopIdentifierExpr();
            case TokenType.InterpolationStart:
                return PopInterpolation();
            case TokenType.BracketOpen:
                return PopLambda();
            case TokenType.CharLiteral
                or TokenType.StringLiteral
                or TokenType.IntLiteral
                or TokenType.FloatLiteral
                or TokenType.BooleanLiteral
                or TokenType.NullLiteral
                or TokenType.SelfLiteral
                or TokenType.ItLiteral
                or TokenType.SquareOpen:
                return PopLiteral();
            default:
                Error($"Unexpected token: {firstToken}", Peek().Span);
                return null!;
        }
    }
    ExpressionNode PopPostfix(bool allowUnparenthesizedBlock = true)
    {
        var expr = PopPrimary();

        while (true)
        {
            var tokenType = Peek().Type;

            if (tokenType is TokenType.Dot)
            {
                expr = PopMemberAccess(expr);
            }
            else if (tokenType is TokenType.ParenthesisOpen)
            {
                expr = PopCall(expr);
            }
            else if (tokenType is TokenType.BracketOpen && allowUnparenthesizedBlock)
            {
                expr = PopCall(expr);
            }
            else if (tokenType is TokenType.SquareOpen)
            {
                expr = PopIndex(expr);
            }
            else
            {
                break;
            }
        }

        return expr;
    }
    ScopedExpressionNode PopScoped()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");
        var expr = PopExpression(); // Cascades back down to the lowest precedence level
        ExpectType(TokenType.ParenthesisClose, "Expected ')'");
        
        return new ScopedExpressionNode(expr, startSpan.To(Peek().Span)); 
    }
    BlockExpressionNode PopBlock()
    {
        var startSpan = Peek().Span;

        bool isSingleLine = false;
        List<StatementNode> statements = [];
        if (Peek().Type is TokenType.EqualArrow)
        {
            isSingleLine = true;
            Pop(); // Pop '=>'
            statements.Add(PopStatement());
        }
        else
        {
            ExpectType(TokenType.BracketOpen, "Expected '{'");

            while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
            {
                statements.Add(PopStatement());
            }
            
            ExpectType(TokenType.BracketClose, "Expected '}'");
        }
        
        return new BlockExpressionNode(isSingleLine, statements.ToValueList(), startSpan.To(Peek().Span));
    }
    IfExpressionNode PopIf()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.IfKeyword, "Expected 'if'");
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");
        var condition = PopExpression();
        ExpectType(TokenType.ParenthesisClose, "Expected ')'");

        var thenBranch = PopBlock();
        
        List<(ExpressionNode condition, BlockExpressionNode body)> elseIfs = [];
        while (Peek().Type is TokenType.ElseKeyword && Peek(1).Type is TokenType.IfKeyword)
        {
            ExpectType(TokenType.ElseKeyword, "Expected 'else'");
            ExpectType(TokenType.IfKeyword, "Expected 'if'");
            
            ExpectType(TokenType.ParenthesisOpen, "Expected '('");
            var elseIfCondition = PopExpression();
            ExpectType(TokenType.ParenthesisClose, "Expected ')'");
            
            var elseIfBranch = PopBlock();
            
            elseIfs.Add((elseIfCondition, elseIfBranch));
        }
        
        BlockExpressionNode? elseBranch = null;
        if (Peek().Type is TokenType.ElseKeyword)
        {
            ExpectType(TokenType.ElseKeyword, "Expected 'else'");
                
            elseBranch = PopBlock();
        }
        
        return new IfExpressionNode(condition, thenBranch, elseIfs.ToValueList(), elseBranch, startSpan.To(Peek().Span));
    }
    ForExpressionNode PopFor()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.ForKeyword, "Expected 'for'");
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");
        
        var paramStart = Peek().Span;
        var name = PopIdentifier();
        var type = AeroType.Auto;
        if (Peek().Type is TokenType.Colon)
        {
            ExpectType(TokenType.Colon, "Expected ':'");
            type = PopType();
        }
        
        ExpectType(TokenType.InKeyword, "Expected 'in'");
        
        var collection = PopExpression();
        ExpectType(TokenType.ParenthesisClose, "Expected ')'");

        var body = PopBlock();
        
        return new ForExpressionNode(new ParamNode(name, type, paramStart.To(Peek().Span)), collection, body, startSpan.To(Peek().Span));
    }
    SpawnExpressionNode PopSpawn()
    {
        var startSpan = Peek().Span;
        ExpectType(TokenType.SpawnKeyword, "Expected 'spawn'");
        
        var body = PopBlock();
        
        return new SpawnExpressionNode(body, startSpan.To(Peek().Span));
    }
    ConcurrentExpressionNode PopConcurrent()
    {
        var startSpan = Peek().Span;
        ExpectType(TokenType.ConcurrentKeyword, "Expected 'concurrent'");
        
        var body = PopBlock();
        
        return new ConcurrentExpressionNode(body, startSpan.To(Peek().Span));
    }
    MatchExpressionNode PopMatch()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.MatchKeyword, "Expected 'match'");
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");
        var target = PopExpression();
        ExpectType(TokenType.ParenthesisClose, "Expected ')'");

        ExpectType(TokenType.BracketOpen, "Expected '{'");

        List<CaseExpressionNode> cases = [];
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            var patternStart = Peek().Span;
            
            var pattern = PopExpression(false);
            var body = PopBlock();

            if (Peek().Type is TokenType.Comma) Pop(); // Pop ','
            
            cases.Add(new CaseExpressionNode(pattern, body, patternStart.To(Peek().Span)));
        }
        ExpectType(TokenType.BracketClose, "Expected '}'");

        return new MatchExpressionNode(target, cases.ToValueList(), startSpan.To(Peek().Span));
    }
    ExpressionNode PopLiteral()
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
                Pop(); // Pop '['

                List<ExpressionNode> elements = [];
                while (Peek().Type is not TokenType.SquareClose and not TokenType.Eof)
                {
                    elements.Add(PopExpression());
                    if (Peek().Type is TokenType.Comma) Pop(); // Pop ','
                }
                ExpectType(TokenType.SquareClose, "Expected ']'");

                return new ArrayLiteralExpressionNode(elements.ToValueList(), startSpan.To(Peek().Span));
        }
        
        if (obj is null) Error("Literal could not be parsed", startSpan);
        else Pop(); // Pop the literal token
        
        return new LiteralExpressionNode(obj ?? 0, token.Type, startSpan.To(Peek().Span));
    }
    IdentifierExpressionNode PopIdentifierExpr()
    {
        var token = ExpectType(TokenType.Identifier, "LogicalNot an identifier");
        return new IdentifierExpressionNode(token.Value, token.Span);
    }
    MemberAccessExpressionNode PopMemberAccess(ExpressionNode source)
    {
        var startSpan = source.Span;
        ExpectType(TokenType.Dot, "Expected '.'");
        var member = PopIdentifierExpr();

        return new MemberAccessExpressionNode(source, member, startSpan.To(Peek().Span));
    }
    CallExpressionNode PopCall(ExpressionNode target)
    {
        var startSpan = target.Span;
        List<ExpressionNode> parameters = [];
        
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            Pop(); // Pop '('

            while (Peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
            {
                parameters.Add(PopExpression());

                if (Peek().Type is TokenType.Comma)
                {
                    Pop();
                }
            }

            ExpectType(TokenType.ParenthesisClose, "Expected ')'");
        }

        // Kotlin style trailing lambda parsing
        if (Peek().Type is TokenType.BracketOpen)
        {
            parameters.Add(PopLambda());
        }

        return new CallExpressionNode(target, parameters.ToValueList(), startSpan.To(Peek().Span));
    }
    IndexExpressionNode PopIndex(ExpressionNode target)
    {
        var startSpan = target.Span;
        
        ExpectType(TokenType.SquareOpen, "Expected '['");
        var index = PopExpression();
        ExpectType(TokenType.SquareClose, "Expected ']'");
        
        return new IndexExpressionNode(target, index, startSpan.To(Peek().Span));
    }
    ExpressionNode PopRange(bool allowUnparenthesizedBlock = true)
    {
        // 1. Handle Prefix Range (..b) or Full Range (..)
        if (IsRangeToken())
        {
            var startSpan = Peek().Span;
            Pop(); // Pop '..'

            ExpressionNode? right = null;
            if (CanStartExpression())
            {
                right = PopBinary(allowUnparenthesizedBlock);
            }

            return new RangeExpressionNode(null, right, startSpan.To(Peek().Span));
        }

        // 2. Parse the left-hand expression
        var left = PopBinary(allowUnparenthesizedBlock);

        // 3. Handle Binary Range (a..b) or Postfix Range (a..)
        if (IsRangeToken())
        {
            var startSpan = left.Span;
            Pop(); // Pop '..'

            ExpressionNode? right = null;
            if (CanStartExpression())
            {
                right = PopBinary(allowUnparenthesizedBlock);
            }

            return new RangeExpressionNode(left, right, startSpan.To(Peek().Span));
        }

        return left;
    }
    ExpressionNode PopBinary(bool allowUnparenthesizedBlock = true)
    {
        var left = PopCast(allowUnparenthesizedBlock);

        while (Peek().Type.IsOperator() && Peek().Type is not TokenType.Dot && Peek().Type is not TokenType.CastSymbol && !IsRangeToken())
        {
            var startSpan = left.Span;
            var op = PopOperator();
            var right = PopCast(allowUnparenthesizedBlock);

            left = new BinaryExpressionNode(left, op, right, startSpan.To(Peek().Span));
        }

        return left;
    }
    ExpressionNode PopCast(bool allowUnparenthesizedBlock = true)
    {
        var expr = PopPostfix(allowUnparenthesizedBlock);

        while (Peek().Type is TokenType.CastSymbol)
        {
            var startSpan = expr.Span;
            Pop(); // Pop '::'

            var target = PopPostfix(allowUnparenthesizedBlock);

            expr = new BinaryExpressionNode(expr, Operator.CastSymbol, target, startSpan.To(Peek().Span));
        }

        return expr;
    }
    UnaryExpressionNode PopUnary()
    {
        var startSpan = Peek().Span;

        bool isPostFix = !Peek().Type.IsOperator();
        Operator op;
        ExpressionNode target;
        if (isPostFix)
        {
            target = PopExpression();
            op = PopOperator();
        }
        else
        {
            op = PopOperator();
            target = PopExpression();
        }

        return new UnaryExpressionNode(op, target, isPostFix, startSpan.To(Peek().Span));
    }
    LambdaExpressionNode PopLambda()
    {
        var startSpan = Peek().Span;
        
        ExpectType(TokenType.BracketOpen, "Expected '{'");

        List<ParamNode> parameters = [];
        if (Peek().Type is TokenType.Identifier) // If there is at least one parameter, then parse the lambda
        {
            while (Peek().Type is not TokenType.ArrowSymbol and not TokenType.Eof)
            {
                var paramStart = Peek().Span;
            
                var name = PopIdentifier();
                var type = AeroType.Auto;
                if (Peek().Type is TokenType.Colon)
                {
                    Pop(); // Pop ':'
                    type = PopType();
                }
            
                parameters.Add(new ParamNode(name, type, paramStart.To(Peek().Span)));
            }
            ExpectType(TokenType.ArrowSymbol, "Expected '->'");
        }
        
        List<StatementNode> statements = [];
        while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
        {
            statements.Add(PopStatement());
        }
        ExpectType(TokenType.BracketClose, "Expected '}'");
        var block = new BlockExpressionNode(false, statements.ToValueList(), startSpan.To(Peek().Span));
        
        return new LambdaExpressionNode(parameters.ToValueList(), block, startSpan.To(Peek().Span));
}
    StringInterpolationExpressionNode PopInterpolation()
    {
        var startSpan = Peek().Span;

        ExpectType(TokenType.InterpolationStart, "Expected '$'");
        List<ExpressionNode> parts = [];

        while (Peek().Type is not TokenType.InterpolationEnd and not TokenType.Eof)
        {
            // 1. Raw string segment fragment
            if (Peek().Type is TokenType.StringLiteral)
            {
                parts.Add(PopLiteral());
            }
            // 2. Embedded expression within braces: ${ expr } or { expr }
            else if (Peek().Type is TokenType.BracketOpen)
            {
                Pop(); // Pop '{'
                parts.Add(PopExpression());
                ExpectType(TokenType.BracketClose, "Expected '}' after interpolated expression");
            }
            // 3. Direct inline expression: $identifier
            else
            {
                parts.Add(PopExpression());
            }
        }

        if (Peek().Type is TokenType.InterpolationEnd)
        {
            Pop(); // Pop closing string delimiter / token
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
    Operator PopOperator()
    {
        var opToken = Peek();
        var op = Operator.Assign;
        if (Peek().Type.IsOperator()) op = Pop().Type.ToOperator();
        else Error($"'{opToken.Value}' is not an operator", Pop().Span);

        return op;
    }
    string PopIdentifier()
    {
        // Make sure that the identifier is not nothing
        var first = ExpectType(TokenType.Identifier, "Identifier not found").Value;
        
        string name = first;
        if (Peek().Type is TokenType.Dot) name += Pop().Value;
        
        while (Peek().Type is TokenType.Identifier)
        {
            name += Pop().Value;

            // Pop the dot between two identifiers and add it
            if (Peek().Type is TokenType.Dot && Peek(1).Type is TokenType.Identifier)
            {
                name += Pop().Value;
            }
        }

        return name;
    }
    AccessMod? PopAccessMod()
    {
        AccessMod? mod = null;
        
        if (Peek().Type is TokenType.AccessModifierKind)
        {
            mod = Pop().Value switch
            {
                "public" => AccessMod.Public,
                "internal" => AccessMod.Internal,
                "protected" => AccessMod.Protected,
                _ => AccessMod.Private
            };
        }
        
        return mod;
    }
    MemberMod PopMemberMod()
    {
        var mod = MemberMod.None;
        
        while (Peek().Type is TokenType.MemberModifierKind)
        {
            mod |= Pop().Value switch
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
    InheritanceMod PopInheritance()
    {
        var mod = InheritanceMod.None;

        if (Peek().Type is TokenType.InheritanceModifierKind)
        {
            mod = Pop().Value switch
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
    VariableKind? PopVarKind()
    {
        VariableKind? mod = null;
        
        if (Peek().Type is TokenType.VariableKind)
        {
            mod = Pop().Value switch
            {
                "var" => VariableKind.Var,
                "const" => VariableKind.Const,
                _ => VariableKind.Val

            };
        }
        
        return mod;
    }
    AeroType PopType()
    {
        AeroType baseType;
        
        bool isRef = Peek().Type is TokenType.RefKeyword;
        if (isRef) Pop();

        // A lambda type
        if (Peek().Type is TokenType.ParenthesisOpen)
        {
            Pop(); // Pop '('

            List<TypeParam> parameters = [];
            while (Peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
            {
                parameters.Add(PopTypeParam());
                if (Peek().Type is TokenType.Comma) Pop(); // Pop ','
            }
            ExpectType(TokenType.ParenthesisClose, "Expected ')'");

            bool isLambdaNullable = Peek().Type is TokenType.Nullable;
            if (isLambdaNullable) Pop();
            
            AeroType returnType = AeroType.Void;
            if (Peek().Type is TokenType.ArrowSymbol)
            {
                Pop(); // Pop '->'
                returnType = PopType();
            }
            
            baseType = new LambdaType(parameters.ToValueList(), returnType, isRef, isLambdaNullable);
        }
        // A scalar / generic type
        else
        {
            if (Peek().Type is not TokenType.Identifier)
            {
                Error("Expected identifier", Peek().Span);
                return AeroType.Error;
            }
            var name = PopIdentifier();
        
            // A generic type
            List<GenericParameterType>? generics = null;
            if (Peek().Type is TokenType.LessThan)
            {
                Pop(); // Pop '<'
                generics = [];
            
                while (Peek().Type is not TokenType.GreaterThan and not TokenType.Eof) // Make sure trailing commas are handled correctly and do not try to
                {
                    // ref G? : String
                
                    bool isParamRef = Peek().Type is TokenType.RefKeyword;
                    if (isParamRef) Pop();

                    var paramName = PopIdentifier();
                
                    bool isParamNullable = Peek().Type is TokenType.Nullable;
                    if (isParamNullable) Pop();

                    AeroType? constraint = null;
                    if (Peek().Type is TokenType.Colon)
                    {
                        Pop(); // Pop ':'
                        constraint = PopType();
                    }
                
                    if (Peek().Type is TokenType.Comma)
                    {
                        Pop(); // Pop ','
                    }

                    generics.Add(new GenericParameterType(paramName, constraint, isParamRef, isParamNullable));
                }
                ExpectType(TokenType.GreaterThan, "Expected '>'");
            }
        
            bool isNullable = Peek().Type is TokenType.Nullable;
            if (isNullable) Pop();
        
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
            Pop(); // Pop '['
            ExpectType(TokenType.SquareClose, "Expected ']'");
            
            bool isArrayNullable = Peek().Type is TokenType.Nullable;
            if (isArrayNullable) Pop();
            
            baseType = new ArrayType(
                ElementType: baseType,
                IsRef: false, 
                IsNullable: isArrayNullable
            );
        }
        
        return baseType with { IsRef = isRef };
    }
    TypeParam PopTypeParam()
    {
        var name = PopIdentifier();
        ExpectType(TokenType.Colon, "Expected ':'");
        var type = PopType();

        return new TypeParam(name, type);
    }
    ValueList<ParamNode> PopParameterDecl()
    {
        ExpectType(TokenType.ParenthesisOpen, "Expected '('");

        var result = new List<ParamNode>();
        while (Peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
        {
            var paramStart = Peek().Span;

            var varKind = PopVarKind() ?? VariableKind.Val;
            var name = PopIdentifier();

            ExpectType(TokenType.Colon, "Expected ':'");
            
            var type = PopType();
            ExpressionNode? init = null;

            if (Peek().Type is TokenType.Equality)
            {
                Pop(); // Pop '='

                init = PopExpression();
            }
            
            result.Add(new ParamNode(name, type, paramStart.To(Peek().Span), init, varKind));
            
            if (Peek().Type is TokenType.Comma) Pop(); // Pop ','
        }
        
        ExpectType(TokenType.ParenthesisClose, "Expected ')'");
        
        return result.ToValueList();
    }
    ValueList<AeroType> PopImplementations()
    {
        List<AeroType> results = [];
        if (Peek().Type is TokenType.Colon)
        {
            Pop(); // Pop ':'
            
            while (Peek().Type is not TokenType.Eof and not TokenType.Semicolon and not TokenType.BracketOpen)
            {
                results.Add(PopType());

                if (Peek().Type is TokenType.Comma) Pop(); // Pop ','
            }
        }

        return results.ToValueList();
    }
    ValueList<GenericParameterType> PopGenericDecls()
    {
        List<GenericParameterType> generics = [];
        if (Peek().Type is TokenType.LessThan)
        {
            Pop(); // Pop '<'
            
            while (Peek().Type is not TokenType.GreaterThan and not TokenType.Eof)
            {
                var startSpan = Peek().Span;

                var name = PopIdentifier();
                AeroType? typeConstraint = null;

                if (Peek().Type is TokenType.Colon)
                {
                    Pop(); // Pop ':'
                    
                    typeConstraint = PopType();
                }
                
                generics.Add(new(name, typeConstraint));

                // Pops
                if (Peek().Type is TokenType.Comma && Peek(1).Type is not TokenType.GreaterThan)
                {
                    Pop(); // Pop commas
                }
            }

            ExpectType(TokenType.GreaterThan, "Expected '>'");
        }

        return generics.ToValueList();
    }
    ValueList<AnnotationStatementNode> PopAnnotations()
    {
        List<AnnotationStatementNode> annotations = [];
        while (Peek().Type is TokenType.At and not TokenType.Eof)
        {
            annotations.Add(PopAnnotation());
        }

        return annotations.ToValueList();
    }
    bool IsStatementTerminator()
    {
        // Explicit terminators or scope closers
        if (Peek().Type is TokenType.Semicolon or TokenType.Eof)
            return true;

        // Check if a line break occurred between the previous Popped token and current token
        return Peek().Span.Start.Line > Peek(-1).Span.End.Line;
    }
    void PopStatementTerminator()
    {
        if (IsStatementTerminator())
        {
            if (Peek().Type is TokenType.Semicolon) Pop();
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
    void ExpectInstance(string[] instanceNames, string errorMessage, SourceSpan? location = null)
    {
        Expect(t => t.Type is TokenType.InstanceKind && instanceNames.Contains(t.Value), errorMessage, location ?? Peek().Span);
    }
    Token ExpectType(TokenType expectedType, string errorMessage, SourceSpan? location = null)
    {
        return Expect(t => t.Type == expectedType, errorMessage, location ?? Peek().Span);
    }
}