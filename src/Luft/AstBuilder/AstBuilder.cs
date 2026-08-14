using Luft.AstBuilder.Ast;
using Luft.Lexer;

namespace Luft.AstBuilder;

internal static class AstBuilder
{
    internal static FileNode BuildAst(List<Token> rawTokens)
    {
        var tokenIndex = 0;
        var tokens = rawTokens.Where(t => t.Type is not TokenType.Whitespace).ToList();
        
        return file();
        
        // Special
        FileNode file()
        {
            var imports = new List<ImportStatementNode>();
            var modules = new List<ModuleDeclarationNode>();

            while (peek().Type is not TokenType.Eof)
            {
                if (peek().Type is TokenType.ModuleKeyword)
                {
                    modules.Add(module());
                }
                else
                {
                    imports.Add(import());
                }
            }

            return new FileNode(modules.ToArray(), imports.ToArray(), peek().Span);
        }
        ModuleDeclarationNode module()
        {
            expectType(TokenType.ModuleKeyword, "Use the 'module' keyword to declare a module.");
            var identifier= consumeIdentifier();
            if (identifier is null)
            {
                return new ModuleDeclarationNode("", [], peek().Span); // empty placeholder
            }
            
            var decls = new List<DeclarationNode>();

            if (peek().Type is TokenType.BracketOpen)
            {
                while (peek().Type is not TokenType.BracketClose and not TokenType.Eof)
                {
                    decls.Add(consumeDecl());
                }
            }
            else
            {
                while (peek().Type is not TokenType.Eof)
                {
                    decls.Add(consumeDecl());
                }
            }
            
            return new ModuleDeclarationNode(identifier, decls.ToArray(), peek().Span);
        }
        ImportStatementNode import()
        {
            
        }
        AnnotationNode annotation()
        {
            var startSpan = peek().Span;
            
            expectType(TokenType.At,"Expected '@' to start annotation.");
            
            var name = expectType(TokenType.Identifier, "Identifier not found.")?.Value;
            if (name == null)
            {
                return new AnnotationNode("", new(), peek().Span);
            }
            
            // Handle parameters if passed
            var parameters = new List<ExpressionNode>();
            if (peek().Type is TokenType.ParenthesisOpen)
            {
                consume(); // Consume '('
                while (peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
                {
                    parameters.Add(consumeExpression());
                    
                    if (peek().Type is TokenType.Comma)
                    {
                        consume(); // Consume ','
                        
                        // Allow trailing comma: @Foo(a, b,)
                        if (peek().Type is TokenType.ParenthesisClose)
                        {
                            break;
                        }
                    }
                    else if (peek().Type is not TokenType.ParenthesisClose)
                    {
                        error("Expected ',' or ')' after parameter.", peek().Span);
                        consume();
                        break; 
                    }
                }

                expectType(TokenType.ParenthesisClose, "Expected ')' to close annotation arguments.");
            }

            return new AnnotationNode(name, parameters.ToValueList(), startSpan.To(peek(-1).Span.End));
        }
        
        
        // Declarations
        DeclarationNode consumeDecl()
        {
            
        }
        FunctionDeclarationNode consumeFunction()
        {
            
        }
        
        // Statements
        
        
        // Expressions
        ExpressionNode consumeExpression()
        {
            
        }
        
        
        // Node Helpers
        string? consumeIdentifier()
        {
            
        }
        TypeRef consumeType()
        {
            bool isRef = peek().Type is TokenType.RefKeyword;
            if (isRef) consume();
            
            string? name = expectType(TokenType.Identifier, "Type-Identifier not found")?.Value;
            if (name is null) return TypeRef.Error;
            
            List<TypeRef> generics = new List<TypeRef>();
            if (peek().Type is TokenType.LessThan)
            {
                consume(); // Consume '<'
                while (peek().Type is not TokenType.GreaterThan) // Make sure trailing commas are handled correctly and do not try to
                {
                    generics.Add(consumeType());
                    
                    if (peek().Type is TokenType.Comma)
                    {
                        consume(); // Consume ','
                        continue;
                    }

                    break;
                }
                
                expectType(TokenType.GreaterThan, "Expected '>' to close type arguments.");                
            }
            
            bool isNullable = peek().Type is TokenType.Nullability;
            if (isNullable) consume();
            
            TypeRef baseType = new TypeRef(
                Name: name, 
                IsRef: isRef, 
                IsNullable: isNullable, 
                TypeArguments: generics.ToValueList()
            );
            
            while (peek().Type is TokenType.SquareOpen)
            {
                consume(); // Consume '['
                ExpressionNode? size = null;
                
                if (peek().Type is not TokenType.SquareClose)
                {
                    size = consumeExpression();
                }
                
                expectType(TokenType.SquareClose, "Expected ']' to close array dimension.");
                
                bool isArrayNullable = peek().Type is TokenType.Nullability;
                if (isArrayNullable) consume();
                
                baseType = new TypeRef(
                    Name: "", 
                    IsRef: false, 
                    IsNullable: isArrayNullable, 
                    ElementType: baseType,
                    ArraySize: size
                );
            }

            return baseType;
        }
        ValueList<ParamNode> consumeParameterDecl()
        {
            var result = new List<ParamNode>();

            while (peek().Type is not TokenType.ParenthesisClose)
            {
                var name = expectType(TokenType.Identifier, "Identifier not found")?.Value;
                if (name is null)
                {
                    result.Add(new ParamNode("_error_", TypeRef.Error));
                    consume(); // Consume bad token
    
                    // consume until a comma or closing paren to re-synchronize
                    while (peek().Type is not TokenType.Comma and not TokenType.ParenthesisClose and not TokenType.Eof)
                    {
                        consume();
                    }
                    if (peek().Type is TokenType.Comma) consume(); // Handle comma
    
                    continue;
                }
                
                var type = TypeRef.AutoVar;
                if (peek().Type is TokenType.Colon) // Type was specified
                {
                    consume(); // Consume the colon
                    type = consumeType();
                }
                
                result.Add(new ParamNode(name, type));
                
                if (peek().Type is TokenType.Comma)
                {
                    consume(); // Consume ','
                    
                    // Allow trailing comma: Foo(a, b,)
                    if (peek().Type is TokenType.ParenthesisClose)
                    {
                        break;
                    }
                }
                else if (peek().Type is not TokenType.ParenthesisClose and not TokenType.Eof)
                {
                    error("Expected ',' or ')' after parameter declaration.", peek().Span);
                    consume();
                    break; 
                }
            }
            
            consume(); // Consume ')'
            
            return result.ToValueList();
        }
        bool isStatementTerminator()
        {
            // Explicit terminators or scope closers
            if (peek().Type is TokenType.Semicolon or TokenType.Eof)
                return true;

            // Check if a line break occurred between the previous consumed token and current token
            return peek().Span.Start.Line > peek(-1).Span.End.Line;
        }
        void consumeStatementTerminator()
        {
            if (isStatementTerminator())
            {
                if (peek().Type is TokenType.Semicolon) consume();
            }
        }
        
        // Helper methods
        Token? expectTerminator(string errorMessage, SourceSpan? location = null) => expect(t => isStatementTerminator(), errorMessage, location);
        Token? expectType(TokenType expectedType, string errorMessage, SourceSpan? location = null) => expect(t => t.Type == expectedType, errorMessage, location);
        Token? expectValue(string expectedValue, string errorMessage, SourceSpan? location = null) => expect(t => t.Value == expectedValue, errorMessage, location);
        Token? expect(Func<Token, bool> condition, string errorMessage, SourceSpan? location = null, bool doConsume)
        {
            if (condition(peek()))
                return doConsume ? consume() : peek();
            
            // First error out, then execute the onError code and return null
            error(errorMessage, location ?? peek().Span);
            return null; // Never reached
        }
        void error(string message, SourceSpan location) => throw new Exception($"{location}: '{message}'"); // ToDo: Implement error collection without throwing
        Token peek(int offset = 0) => tokenIndex + offset < tokens.Count ? tokens[tokenIndex + offset] : tokens.Last();
        Token consume()
        {
            var token = peek();
            tokenIndex++;
            return token;
        }
    }
}