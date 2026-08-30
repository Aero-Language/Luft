using System.Xml.Schema;
using Luft.AstBuilder.Ast;
using Luft.Lexer;
using Luft.Utility;

namespace Luft.AstBuilder;

public static class Builder
{
    private static readonly TokenType[] Excluded = [TokenType.Whitespace, TokenType.Comment, TokenType.Unknown];
    
    public static FileNode BuildAst(List<Token> rawTokens)
    {
        var tokenIndex = 0;
        var tokens = rawTokens.Where(t => !Excluded.Contains(t.Type)).ToList();
        
        return File();
        
        // Special
        FileNode File()
        {
            var imports = new List<ImportStatementNode>();
            var modules = new List<ModuleDeclarationNode>();

            while (Peek().Type is not TokenType.Eof)
            {
                if (Peek().Type is TokenType.ModuleKeyword)
                {
                    modules.Add(Module());
                }
                else
                {
                    imports.Add(Import());
                }
            }

            return new FileNode(modules.ToArray(), imports.ToArray(), Peek().Span);
        }
        ModuleDeclarationNode Module()
        {
            ExpectType(TokenType.ModuleKeyword, "Use the 'module' keyword to declare a module.");
            var identifier= ConsumeIdentifier();
            var decls = new List<DeclarationNode>();

            if (Peek().Type is TokenType.BracketOpen)
            {
                while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
                {
                    decls.Add(ConsumeDecl());
                }
            }
            else
            {
                while (Peek().Type is not TokenType.Eof)
                {
                    decls.Add(ConsumeDecl());
                }
            }
            
            return new ModuleDeclarationNode(identifier, decls.ToArray(), Peek().Span);
        }
        ImportStatementNode Import()
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
                ExpectType(TokenType.ImportKeyword, "Import keyword not found.");
            }

            var identifier = ConsumeIdentifier();
            
            if (isFrom)
            {
                ExpectType(TokenType.ImportKeyword, "Import keyword not found.");

                List<string> subImports = [];
                
                while (Peek().Type is TokenType.Identifier or TokenType.Eof)
                {
                    subImports.Add(Consume().Value);
                }
                
                return new ImportStatementNode(identifier, subImports.ToValueList(), start.To(Peek().Span.End));
            }

            return new ImportStatementNode(identifier, [], start.To(Peek().Span.End));
        }
        AnnotationNode ConsumeAnnotation()
        {
            var startSpan = Peek().Span;
            
            ExpectType(TokenType.At,"Expected '@' to start annotation.");
            
            var name = ExpectType(TokenType.Identifier, "Identifier not found.")?.Value;
            if (name is null)
            {
                return new AnnotationNode("", new(), startSpan.To(Peek().Span.End));
            }
            
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
            var peekCursor = InstancePeek();
            
            List<AnnotationNode> annotations = [];
            while (Peek().Type is TokenType.At and not TokenType.Eof)
            {
                annotations.Add(ConsumeAnnotation());
            }

            var annots = annotations.ToValueList();
            
            if (peekCursor != -1)
            {
                var kind = Peek(peekCursor);
                if (kind.Type is not TokenType.InstanceKind) Error("Declaration kind could not be predicted", kind.Span);
                
                return kind.Value switch
                {
                    "struct" => ConsumeStruct(annots),
                    "record" => ConsumeRecord(annots),
                    "class" => ConsumeClass(annots),
                    "trait" => ConsumeTrait(annots),
                    "enum" => ConsumeEnum(annots),
                    "annotation" => ConsumeAnnotationDecl(annots),
                    "fun" => ConsumeFunction(annots),
                    "extension" => ConsumeExtension(annots),
                    _ => new ErrorDeclarationNode(Peek().Span)
                };
            }
            else
            {
                bool isVar = Peek().Type is TokenType.VariableKind;
                
                return isVar ? ConsumeVariable(annots) : ConsumeProperty(annots);
            }
        }
        FunctionDeclarationNode ConsumeFunction(ValueList<AnnotationNode>? annotations = null)
        {
            var startSpan = Peek().Span;
            
            var access = ConsumeAccessMod() ?? AccessModExtensions.FunctionDefault;
            var memberMod = ConsumeMemberMod();

            if (Peek().Type is TokenType.InstanceKind && Peek().Value == "extension") Consume(); // Consume 'extension' 
            ExpectType(TokenType.FunctionKeyword, "Function keyword not found.");

            var name = ConsumeIdentifier();
            
            var generics = ConsumeGenericDecls();
            
            ExpectType(TokenType.ParenthesisOpen, "Opening of function Arguments was expected.");
            var parameters = ConsumeParameterDecl();

            var returning = TypeRef.Void;
            if (Peek().Type is TokenType.ArrowSymbol) returning = ConsumeType();

            BlockExpressionNode body = ConsumeBlock();
            
            return new FunctionDeclarationNode(annotations.OrNew(), access, memberMod, returning, name, generics.ToValueList(), parameters, body, startSpan.To(Peek().Span.End));
        }
        ExtensionDeclarationNode ConsumeExtension(ValueList<AnnotationNode>? annotations = null)
        {
            var peekCursor = InstancePeek();

            string targetType;
            DeclarationNode decl;
            if (peekCursor == -1)
            {
                var node = ConsumeProperty(annotations);
                decl = node;
                targetType = node.Name.FirstIdentifier();
            }
            else
            {
                var node = ConsumeFunction(annotations);
                decl = node;
                targetType = node.Name.FirstIdentifier();
            }

            return new ExtensionDeclarationNode(decl, targetType.ToType(), decl.Span);
        }
        StructDeclarationNode ConsumeStruct(ValueList<AnnotationNode>? annotations = null)
        {
            var startSpan = Peek().Span;
            
            var access = ConsumeAccessMod() ?? AccessModExtensions.StructDeclDefault;
            var memberMod = ConsumeMemberMod();

            // Make sure the struct keyword was used
            Expect((token) => token.Type is TokenType.InstanceKind && token.Value == "struct", "Expected 'struct'");

            var name = ConsumeIdentifier();

            List<DeclarationNode> decls = [];

            if (Peek().Type is TokenType.ParenthesisOpen)
            {
                decls.AddRange(ConsumePrimaryConstructor(true));
            }

            var implementations = ConsumeImplementations();
            
            while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
            {
                decls.Add(ConsumeDecl());
            }
            
            return new StructDeclarationNode(annotations.OrNew(), access, memberMod, name, decls.ToValueList(), implementations, startSpan.To(Peek().Span));
        }
        RecordDeclarationNode ConsumeRecord(ValueList<AnnotationNode>? annotations = null)
        {
            var startSpan = Peek().Span;
            
            var access = ConsumeAccessMod() ?? AccessModExtensions.StructDeclDefault;
            var memberMod = ConsumeMemberMod();

            // Make sure the record keyword was used
            Expect((token) => token.Type is TokenType.InstanceKind && token.Value == "record", "Expected 'record'");
            
            var name = ConsumeIdentifier();
            
            var generics = ConsumeGenericDecls();
            
            List<DeclarationNode> decls = [];
            if (Peek().Type is TokenType.ParenthesisOpen)
            {
                decls.AddRange(ConsumePrimaryConstructor(true));
            }

            var implementations = ConsumeImplementations();
            
            while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
            {
                decls.Add(ConsumeDecl());
            }
            
            return new RecordDeclarationNode(annotations.OrNew(), access, memberMod, name, generics, decls.ToValueList(), implementations, startSpan.To(Peek().Span));
        }
        AnnotationDeclarationNode ConsumeAnnotationDecl(ValueList<AnnotationNode>? annotations = null)
        {
            var startSpan = Peek().Span;
            
            var access = ConsumeAccessMod() ?? AccessModExtensions.StructDeclDefault;
            var memberMod = ConsumeMemberMod();

            // Make sure the annotation keyword was used
            Expect((token) => token.Type is TokenType.InstanceKind && token.Value == "annotation", "Expected 'annotation'");
            
            var name = ConsumeIdentifier();
            
            var generics = ConsumeGenericDecls();
            
            List<DeclarationNode> decls = [];
            if (Peek().Type is TokenType.ParenthesisOpen)
            {
                decls.AddRange(ConsumePrimaryConstructor(true));
            }
            
            return new AnnotationDeclarationNode(annotations.OrNew(), access, memberMod, name, generics, decls.ToValueList(), startSpan.To(Peek().Span));
        }
        ClassDeclarationNode ConsumeClass(ValueList<AnnotationNode>? annotations = null)
        {
            var startSpan = Peek().Span;
            
            var access = ConsumeAccessMod() ?? AccessModExtensions.ClassDeclDefault;
            var memberMod = ConsumeMemberMod();

            // Make sure the class keyword was used
            Expect((token) => token.Type is TokenType.InstanceKind && token.Value == "class", "Expected 'class'");
            
            var name = ConsumeIdentifier();
            
            var generics = ConsumeGenericDecls();
            
            List<DeclarationNode> decls = [];
            if (Peek().Type is TokenType.ParenthesisOpen)
            {
                decls.AddRange(ConsumePrimaryConstructor(true));
            }

            var implementations = ConsumeImplementations();
            
            while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
            {
                decls.Add(ConsumeDecl());
            }
            
            return new ClassDeclarationNode(annotations.OrNew(), access, memberMod, name, generics, decls.ToValueList(), implementations, startSpan.To(Peek().Span));
        }
        TraitDeclarationNode ConsumeTrait(ValueList<AnnotationNode>? annotations = null)
        {
            var startSpan = Peek().Span;
            
            var access = ConsumeAccessMod() ?? AccessModExtensions.ClassDeclDefault;

            // Make sure the class keyword was used
            Expect((token) => token.Type is TokenType.InstanceKind && token.Value == "trait", "Expected 'trait'");
            
            var name = ConsumeIdentifier();
            var generics = ConsumeGenericDecls();
            var implementations = ConsumeImplementations();
            
            List<DeclarationNode> decls = [];
            while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
            {
                decls.Add(ConsumeDecl());
            }
            
            return new TraitDeclarationNode(annotations.OrNew(), access, name, generics, decls.ToValueList(), implementations, startSpan.To(Peek().Span));
        }
        EnumDeclarationNode ConsumeEnum(ValueList<AnnotationNode>? annotations = null)
        {
            var startSpan = Peek().Span;
            
            var access = ConsumeAccessMod() ?? AccessModExtensions.ClassDeclDefault;

            // Make sure the enum keyword was used and check if its an enum class
            Expect((token) => token.Type is TokenType.InstanceKind && token.Value == "enum", "Expected 'enum'");
            bool isEnumClass = Peek().Type is TokenType.InstanceKind && Peek().Value == "class";
            if (isEnumClass) Consume(); // Consume 'class'

            var name = ConsumeIdentifier();

            ValueList<VariableDeclarationNode>? memberValues = null;
            if (Peek().Type is TokenType.ParenthesisOpen)
            {
                memberValues = ConsumePrimaryConstructor(true);
            }
            
            TypeRef? memberType = null;
            if (Peek().Type is TokenType.Colon)
            {
                Consume(); // Consume ':'
                memberType = ConsumeIdentifier().ToType();
            }
            
            List<EnumMemberNode> members = [];
            while (Peek().Type is not TokenType.BracketClose and not TokenType.Eof)
            {
                var memberStartSpan = Peek().Span;
                
                var memberName = ConsumeIdentifier();
                ExpressionNode? memberValue = null;
                if (Peek().Type is TokenType.Assign)
                {
                    memberValue = ConsumeExpression();
                }
                members.Add(new EnumMemberNode(memberName, memberValue, memberStartSpan.To(Peek().Span)));
                
                if (Peek().Type is TokenType.Comma) Consume(); // Consume ','
            }
            
            return new EnumDeclarationNode(annotations.OrNew(), access, name, memberType, memberValues, members.ToValueList(),  startSpan.To(Peek().Span));
        }
        PropertyDeclarationNode ConsumeProperty(ValueList<AnnotationNode>? annotations = null)
        {
            var startSpan = Peek().Span;
            
            var access = ConsumeAccessMod() ?? AccessMod.Public;
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
                    var accessorBlock = ConsumeBlock();
                    var accessor = new PropertyAccessorNode(accessorAccess, accessorBlock, accessorStart);

                    if (accessorType is TokenType.SetKeyword) setter = accessor;
                    else getter = accessor;
                }
                else
                {
                    Error("You can only declare Property accessors here.", Peek().Span);
                }
            }
            
            ExpectType(TokenType.BracketClose, "Expected '}'");
            
            ExpressionNode? init = null;
            if (Peek().Type is TokenType.Assign)
            {
                Consume(); // Consume '='
                init = ConsumeExpression();
            }
            
            return new PropertyDeclarationNode(annotations.OrNew(), access, type, name, getter, setter, init, startSpan.To(Peek().Span));
        }
        VariableDeclarationNode ConsumeVariable(ValueList<AnnotationNode>? annotations = null)
        {
            var startSpan = Peek().Span;
            
            var access = ConsumeAccessMod() ?? AccessMod.Private;
            var varKind = ConsumeVarKind() ?? VariableKind.Val;
            var name = ConsumeIdentifier();

            var type = TypeRef.AutoVar;
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
            
            return new VariableDeclarationNode(annotations.OrNew(), access, varKind, type, name, init, startSpan.To(Peek().Span));
        }
        
        // Statements
        
        
        // Expressions
        ExpressionNode ConsumeExpression()
        {
            return null; // ToDo: Implement
        }

        BlockExpressionNode ConsumeBlock()
        {
            return null; // ToDo: Implement, keep the singleStatementBlock in mind here
        }
        
        
        // Node Helpers
        string ConsumeIdentifier()
        {
            // Make sure that the identifier is not nothing
            var first = ExpectType(TokenType.Identifier, "Identifier not found")?.Value;
            if (first is null) return string.Empty;
            
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
        TypeRef ConsumeType()
        {
            bool isRef = Peek().Type is TokenType.RefKeyword;
            if (isRef) Consume();
            
            string? name = ExpectType(TokenType.Identifier, "Type-Identifier not found")?.Value;
            if (name is null) return TypeRef.Error;
            
            List<TypeRef> generics = new List<TypeRef>();
            if (Peek().Type is TokenType.LessThan)
            {
                Consume(); // Consume '<'
                while (Peek().Type is not TokenType.GreaterThan) // Make sure trailing commas are handled correctly and do not try to
                {
                    generics.Add(ConsumeType());
                    
                    if (Peek().Type is TokenType.Comma)
                    {
                        Consume(); // Consume ','
                        continue;
                    }

                    break;
                }
                
                ExpectType(TokenType.GreaterThan, "Expected '>' to close type arguments.");                
            }
            
            bool isNullable = Peek().Type is TokenType.Nullability;
            if (isNullable) Consume();
            
            TypeRef baseType = new TypeRef(
                Name: name, 
                IsRef: isRef, 
                IsNullable: isNullable, 
                TypeArguments: generics.ToValueList()
            );
            
            while (Peek().Type is TokenType.SquareOpen)
            {
                Consume(); // Consume '['
                
                ExpectType(TokenType.SquareClose, "Expected ']' to close array dimension.");
                
                bool isArrayNullable = Peek().Type is TokenType.Nullability;
                if (isArrayNullable) Consume();
                
                baseType = new TypeRef(
                    Name: "", 
                    IsRef: false, 
                    IsNullable: isArrayNullable, 
                    ElementType: baseType
                );
            }

            return baseType;
        }
        ValueList<VariableDeclarationNode> ConsumePrimaryConstructor(bool canDeclareVarKind = false)
        {
            var result = new List<VariableDeclarationNode>();

            ExpectType(TokenType.ParenthesisOpen, "Expected '('");

            while (Peek().Type is not TokenType.ParenthesisClose)
            {
                var paramStart = Peek().Span;
                
                var varKindNull = ConsumeVarKind();
                var varKind = varKindNull ?? VariableKind.Val;

                if (!canDeclareVarKind && varKindNull is not null)
                {
                    Error("You can not specify this here.", Peek(-1).Span);
                    varKind = VariableKind.Val;
                }
                
                var name = ExpectType(TokenType.Identifier, "Identifier not found")?.Value;
                if (name is null)
                {
                    // ToDo: Completely wrong, we need to do this as a constructor with 'IsPrimary = true'
                    result.Add(new VariableDeclarationNode([], varKind, TypeRef.Error, "_error_", null, paramStart.To(Peek().Span)));
                    Consume(); // Consume bad token
    
                    // consume until a comma or closing paren to re-synchronize
                    while (Peek().Type is not TokenType.Comma and not TokenType.ParenthesisClose and not TokenType.Eof)
                    {
                        Consume();
                    }
                    if (Peek().Type is TokenType.Comma) Consume(); // Handle comma
    
                    continue;
                }

                ExpectType(TokenType.Colon, "Expected ':'");
                
                var type = ConsumeType();
                ExpressionNode? init = null;

                if (Peek().Type is TokenType.Equality)
                {
                    Consume(); // Consume '='

                    init = ConsumeExpression();
                }
                
                result.Add(new VariableDeclarationNode([], varKind, type, name, init, paramStart.To(Peek().Span)));
                
                if (Peek().Type is TokenType.Comma)
                {
                    Consume(); // Consume ','
                    
                    // Allow trailing comma: Foo(a, b,)
                    if (Peek().Type is TokenType.ParenthesisClose)
                    {
                        break;
                    }
                }
                else if (Peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
                {
                    Error("Expected ',' or ')' after parameter declaration.", Peek().Span);
                    Consume(); // Consume ',' or ')'
                    break; 
                }
            }
            
            Consume(); // Consume ')'
            
            return result.ToValueList();
        }
        ValueList<ParamNode> ConsumeParameterDecl()
        {
            var result = new List<ParamNode>();

            ExpectType(TokenType.ParenthesisOpen, "Expected '('");

            while (Peek().Type is not TokenType.ParenthesisClose)
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
                
                result.Add(new ParamNode(varKind, name, type, init));
                
                if (Peek().Type is TokenType.Comma)
                {
                    Consume(); // Consume ','
                    
                    // Allow trailing comma: Foo(a, b,)
                    if (Peek().Type is TokenType.ParenthesisClose)
                    {
                        break;
                    }
                }
                else if (Peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
                {
                    Error("Expected ',' or ')' after parameter declaration.", Peek().Span);
                    Consume(); // Consume ',' or ')'
                    break; 
                }
            }
            
            Consume(); // Consume ')'
            
            return result.ToValueList();
        }
        ValueList<TypeRef> ConsumeImplementations()
        {
            List<TypeRef> results = [];

            if (Peek().Type is TokenType.Colon)
            {
                while (Peek().Type is not TokenType.Eof and not TokenType.Semicolon and not TokenType.BracketOpen)
                {
                    results.Add(ConsumeIdentifier().ToType());

                    if (Peek().Type is TokenType.Comma)
                    {
                        Consume(); // Consume the comma
                    }
                }
            }

            return results.ToValueList();
        }
        ValueList<GenericParamNode> ConsumeGenericDecls()
        {
            List<GenericParamNode> generics = [];
            if (Peek().Type is TokenType.LessThan)
            {
                while (Peek().Type is not TokenType.GreaterThan and not TokenType.Eof)
                {
                    var startSpan = Peek().Span;

                    var name = ConsumeIdentifier();
                    TypeRef typeConstraint = TypeRef.Void;

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
            }

            return generics.ToValueList();
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
        int InstancePeek()
        {
            // ToDo: Find actual max value here
            int maxPeek = Enum.GetNames<MemberMod>().Length + 4;
            int peekCursor = 0;

            while (Peek(peekCursor).Type is not TokenType.InstanceKind and not TokenType.Eof)
            {
                peekCursor++;

                if (peekCursor > maxPeek)
                {
                    peekCursor = -1;
                    break;
                }
            }

            return peekCursor;
        }
        
        // Helper methods
        Token? ExpectTerminator(string errorMessage, SourceSpan? location = null) => Expect(_ => IsStatementTerminator(), errorMessage, location);
        Token? ExpectType(TokenType expectedType, string errorMessage, SourceSpan? location = null) => Expect(t => t.Type == expectedType, errorMessage, location);
        Token? ExpectValue(string expectedValue, string errorMessage, SourceSpan? location = null) => Expect(t => t.Value == expectedValue, errorMessage, location);
        Token? Expect(Func<Token, bool> condition, string errorMessage, SourceSpan? location = null, bool doConsume = true)
        {
            if (condition(Peek()))
                return doConsume ? Consume() : Peek();
            
            // First error out, then execute the onError code and return null
            Error(errorMessage, location ?? Peek().Span);
            return null; // Never reached
        }
        void Error(string message, SourceSpan location) => throw new Exception($"{location}: '{message}'"); // ToDo: Implement error collection without throwing
        Token Peek(int offset = 0) => tokenIndex + offset < tokens.Count ? tokens[tokenIndex + offset] : tokens.Last();
        Token Consume()
        {
            var token = Peek();
            tokenIndex++;
            return token;
        }
    }
}