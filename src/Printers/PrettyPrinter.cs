using System.Globalization;
using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.Utility;

namespace Luft.Printers;

public class PrettyPrinter(int indentAmount = 4, bool isLib = false) : PrinterBase(indentAmount)
{
    #region Special

    protected override string VisitFile(FileNode node)
    {
        // Root-level content is never indented, so no Incr/Decr/Indent here.
        var text = string.Join("", node.Imports.Select(i => Visit(i) + '\n')) + "\n";
        if (node.Modules.Any()) text += string.Join("\n", node.Modules.Select(Visit));

        return text;
    }
    protected override string VisitPropertyAccessor(PropertyAccessorNode node)
    {
        var text = $"{node.AccessMod.AsString()} {node.Kind.AsString()}";
        if (node.Body is not null)
        {
            var body = Visit(node.Body);
            text += body.TrimEnd().StartsWith("=>")
                ? $" {body.TrimEnd()};" // Only append semicolon if it's the short form
                : $" {body}";
        }
        else text += ";";

        return text;
    }
    protected override string VisitParam(ParamNode node)
    {
        var text = $"{node.VarKind.AsString()} {node.Name}";
        if (node.Type != AeroType.Auto) text += $": {node.Type}";
        if (node.Initializer is not null) text += $" = {Visit(node.Initializer)}";
        return text;
    }

    #endregion
    
    #region Declarations

    protected override string VisitFunction(FunctionDeclarationNode node)
    {
        var text = $"{string.Join("", node.Annotations.Select(Visit))}{(node.Annotations.Any() ? Indent() : "")}{node.AccessMod.AsString()} ";
        var memberMods = node.MemberMods.AsString();
        if (memberMods is not null) text += $"{memberMods} ";
        if (node.InheritanceMod is not InheritanceMod.None) text += $"{node.InheritanceMod.AsString()} ";
        if (node.Name.IdentifierParts().Length > 1) text += "extension "; // Handle Extensions
        text += $"fun {node.Name}";

        if (node.GenericParameters.Any())
        {
            text += $"<{string.Join(", ", node.GenericParameters)}>";
        }

        text += $"({string.Join(", ", node.Parameters.Select(Visit))})";
        
        if (node.ReturnType != AeroType.Auto) text += $" -> {node.ReturnType}";

        if (node.Body is not null) text += $" {Visit(node.Body)}";

        return text;
    }
    protected override string VisitExtension(ExtensionDeclarationNode ext)
    {
        if (ext.Extension is FunctionDeclarationNode fun) return Visit(fun);
        else if (ext.Extension is PropertyDeclarationNode prop) return Visit(prop);
        else return Default(ext);
    }
    protected override string VisitExtensionBlock(ExtensionBlockDeclarationNode node)
    {
        Incr();
        var body = string.Join("", node.Extensions.Select(e => Indent(Visit(e))));
        Decr();

        return $"{node.AccessMod.AsString()} extensions {node.TargetType} {{\n{body}\n{Indent("}")}";
    }
    protected override string VisitStruct(StructDeclarationNode node)
    {
        var text = $"{string.Join("", node.Annotations.Select(Visit))}{(node.Annotations.Any() ? Indent() : "")}{node.AccessMod.AsString()} ";
        var memberMods = node.MemberMods.AsString();
        if (memberMods is not null) text += $"{memberMods} ";
        if (node.InheritanceMod is not InheritanceMod.None) text += $"{node.InheritanceMod.AsString()} ";
        
        text += $"struct {node.Name}";
        if (node.Declarations.Any(n => n is PrimaryConstructorDeclarationNode)) text += Visit(node.Declarations.OfType<PrimaryConstructorDeclarationNode>().First());
        if (node.Implements.Any()) text += $": {string.Join(", ", node.Implements)}";

        Incr();
        var body = string.Join("\n", node.Declarations.Where(n => n is not PrimaryConstructorDeclarationNode).Select(d => Indent(Visit(d))));
        Decr();

        text += $" {{\n{body}\n{Indent("}")}";

        return text;
    }
    protected override string VisitRecord(RecordDeclarationNode node)
    {
        var text = $"{string.Join("", node.Annotations.Select(Visit))}{(node.Annotations.Any() ? Indent() : "")}{node.AccessMod.AsString()} ";
        var memberMods = node.MemberMods.AsString();
        if (memberMods is not null) text += $"{memberMods} ";
        if (node.InheritanceMod is not InheritanceMod.None) text += $"{node.InheritanceMod.AsString()} ";
        
        text += $"record {node.Name}";
        if (node.GenericParameters.Any()) text += $"<{string.Join(", ", node.GenericParameters)}>";
        if (node.Declarations.Any(n => n is PrimaryConstructorDeclarationNode)) text += Visit(node.Declarations.OfType<PrimaryConstructorDeclarationNode>().First());
        if (node.Implements.Any()) text += $": {string.Join(", ", node.Implements)}";

        Incr();
        var body = string.Join("\n", node.Declarations.Where(n => n is not PrimaryConstructorDeclarationNode).Select(d => Indent(Visit(d))));
        Decr();

        text += $" {{\n{body}\n{Indent("}")}";

        return text;
    }
    protected override string VisitAnnotationDecl(AnnotationDeclarationNode node)
    {
        var text = $"{string.Join("", node.Annotations.Select(Visit))}{(node.Annotations.Any() ? Indent() : "")}{node.AccessMod.AsString()} ";
        var memberMods = node.MemberMods.AsString();
        if (memberMods is not null) text += $"{memberMods} ";
        
        text += $"annotation {node.Name}";
        if (node.GenericParameters.Any()) text += $"<{string.Join(", ", node.GenericParameters)}>";
        if (node.Declarations.Any(n => n is PrimaryConstructorDeclarationNode)) text += Visit(node.Declarations.OfType<PrimaryConstructorDeclarationNode>().First());

        Incr();
        var body = string.Join("\n", node.Declarations.Where(n => n is not PrimaryConstructorDeclarationNode).Select(d => Indent(Visit(d))));
        Decr();

        text += $" {{\n{body}\n{Indent("}")}";
        
        return text;
    }
    protected override string VisitClass(ClassDeclarationNode node)
    {
        var text = $"{string.Join("", node.Annotations.Select(Visit))}{(node.Annotations.Any() ? Indent() : "")}{node.AccessMod.AsString()} ";
        var memberMods = node.MemberMods.AsString();
        if (memberMods is not null) text += $"{memberMods} ";
        if (node.InheritanceMod is not InheritanceMod.None) text += $"{node.InheritanceMod.AsString()} ";
        
        text += $"class {node.Name}";
        if (node.GenericParameters.Any()) text += $"<{string.Join(", ", node.GenericParameters)}>";
        if (node.Declarations.Any(n => n is PrimaryConstructorDeclarationNode)) text += Visit(node.Declarations.OfType<PrimaryConstructorDeclarationNode>().First());
        if (node.Implements.Any()) text += $": {string.Join(", ", node.Implements)}";

        Incr();
        var body = string.Join("\n", node.Declarations.Where(n => n is not PrimaryConstructorDeclarationNode).Select(d => Indent(Visit(d))));
        Decr();

        text += $" {{\n{body}\n{Indent("}")}";
        
        return text;
    }
    protected override string VisitTrait(TraitDeclarationNode node)
    {
        var text = $"{string.Join("", node.Annotations.Select(Visit))}{(node.Annotations.Any() ? Indent() : "")}{node.AccessMod.AsString()} ";
        if (node.InheritanceMod is not InheritanceMod.None) text += $"{node.InheritanceMod.AsString()} ";
        
        text += $"trait {node.Name}";
        if (node.GenericParameters.Any()) text += $"<{string.Join(", ", node.GenericParameters)}>";
        if (node.Traits.Any()) text += $": {string.Join(", ", node.Traits)}";

        Incr();
        var body = string.Join("\n", node.Declarations.Where(n => n is not PrimaryConstructorDeclarationNode).Select(d => Indent(Visit(d))));
        Decr();

        text += $" {{\n{body}\n{Indent("}")}";
        
        return text;
    }
    protected override string VisitEnumMember(EnumMemberNode node)
    {
        var text = $"{node.Name}";
        if (node.Value is not null) text += $" = {Visit(node.Value)}";

        return text;
    }
    protected override string VisitEnum(EnumDeclarationNode node)
    {
        var text = $"{string.Join("", node.Annotations.Select(Visit))}{(node.Annotations.Any() ? Indent() : "")}{node.AccessMod.AsString()} ";
        
        text += $"enum {(node.IsEnumClass ? "class " : "")}{node.Name}";
        if (node.Parameters != null) text += $"({string.Join(", ", node.Parameters.Select(Visit))})";
        if (node.MemberType != null) text += $": {node.MemberType}";

        Incr();
        var body = string.Join(",\n", node.Members.Select(m => Indent(Visit(m))));
        Decr();

        text += $" {{\n{body}\n{Indent("}")}";
        
        return text;
    }
    protected override string VisitProperty(PropertyDeclarationNode node)
    {
        var text = $"{string.Join("", node.Annotations)}{node.AccessMod.AsString()} ";
        var memberMods = node.MemberMods.AsString();
        if (memberMods is not null) text += $"{memberMods} ";
        if (node.InheritanceMod is not InheritanceMod.None) text += $"{node.InheritanceMod.AsString()} ";
        text += $"{node.Name}: {node.Type} {{\n";

        Incr();
        var accessors = new List<string>();
        if (node.Getter is not null) accessors.Add(Indent(Visit(node.Getter)));
        if (node.Setter is not null) accessors.Add(Indent(Visit(node.Setter)));
        text += string.Join("\n", accessors);
        Decr();

        text += $"\n{Indent("}")}";
        if (node.Initializer is not null) text += $" = {Visit(node.Initializer)}";
        
        return text;
    }
    protected override string VisitField(FieldDeclarationNode node)
    {
        var text = $"{string.Join("", node.Annotations)}{node.AccessMod.AsString()} {node.VarKind.AsString()} ";
        var memberMods = node.MemberMods.AsString();
        if (memberMods is not null) text += $"{memberMods} ";
        if (node.InheritanceMod is not InheritanceMod.None) text += $"{node.InheritanceMod.AsString()} ";
        
        text += $"{node.Name}";
        if (node.Type != AeroType.Auto) text += $": {node.Type}";
        if (node.Initializer is not null) text += $" = {Visit(node.Initializer)}";
        
        return text;
    }
    protected override string VisitPrimaryConstructor(PrimaryConstructorDeclarationNode node)
    {
        return $"({string.Join(", ", node.Variables.Select(v => Visit(v).TrimEnd('\n')))})";
    }
    protected override string VisitConstructor(ConstructorDeclarationNode node)
    {
        var text = $"{string.Join("", node.Annotations)}{node.AccessMod.AsString()} ";
        text += $"constructor {node.Name}";

        text += $"({string.Join(", ", node.Parameters.Select(Visit))})";
        
        if (node.Body is not null) text += $" {Visit(node.Body)}";

        return text;
    }
    protected override string VisitDestructor(DestructorDeclarationNode node)
    {
        var text = $"{string.Join("", node.Annotations)}";
        text += $"destructor {node.Name}()";
        
        if (node.Body is not null) text += $" {Visit(node.Body)}";

        return text;
    }
    protected override string VisitModule(ModuleDeclarationNode node)
    {
        Incr();
        var body = string.Join("\n\n", node.Declarations.Select(d => Indent(Visit(d))));
        Decr();

        if (node.ModulePath != "") return $"module {node.ModulePath} {{\n{body}\n{Indent("}")}";
        return body;
    }

    #endregion
    
    #region Statements

    protected override string VisitAnnotation(AnnotationStatementNode node)
    {
        var text = $"@{node.Name}";
        
        if (node.Parameters.Any()) text += $"({string.Join(", ", node.Parameters.Select(Visit))})";

        return text + '\n';
    }
    protected override string VisitVariable(VariableStatementNode node)
    {
        var text = $"{node.VarKind.AsString()} {node.Name}";
        if (node.Type != AeroType.Auto) text += $": {node.Type}";
        if (node.Initializer != null) text +=  $" = {Visit(node.Initializer)}";

        return text;
    }
    protected override string VisitReturn(ReturnStatementNode node)
    {
        var text = "return";
        if (node.Value is not null) text += $" {Visit(node.Value)}";

        return text;
    }
    protected override string VisitBreak(BreakStatementNode node)
    {
        return "break";
    }
    protected override string VisitContinue(ContinueStatementNode node)
    {
        return "continue";
    }
    protected override string VisitWhile(WhileStatementNode node)
    {
        return $"while ({Visit(node.Condition)}) {Visit(node.Body)}";
    }
    protected override string VisitExpression(ExpressionStatementNode node)
    {
        return Visit(node.Expression);
    }
    protected override string VisitImport(ImportStatementNode node)
    {
        if (node.Imports.Any())
        {
            return $"from {node.TargetPath} import {string.Join(", ", node.Imports)}";
        }

        return $"import {node.TargetPath}";
    }
    protected override string VisitAssignment(AssignmentStatementNode node)
    {
        return $"{Visit(node.Target)} {node.Operator.AsString()} {Visit(node.Value)}";
    }

    #endregion
    
    #region Expressions

    protected override string VisitBlock(BlockExpressionNode node)
    {
        if (node.Statements.Count == 0 || isLib) return "{ }";
        if (!node.IsSingleLine)
        {
            bool useNl = node.Statements.Count == 1 && node.Statements.SingleOrDefault() is ExpressionStatementNode { Expression: CallExpressionNode c } && c.Arguments.LastOrDefault() is LambdaExpressionNode;
            if (!useNl) useNl = node.Statements.Count > 1;
            
            Incr();
            var body = string.Join(useNl ? "\n" : "", node.Statements.Select(s => Indent(Visit(s))));
            Decr();

            if (useNl) return $"{{\n{body}\n{Indent("}")}";
            return $"{{ {body.TrimStart()} }}";
        }

        return $"=> {string.Join("", node.Statements.Select(Visit))}".TrimEnd() + '\n';
    }
    protected override string VisitIf(IfExpressionNode node)
    {
        var text = $"if ({Visit(node.Condition)}) {Visit(node.ThenBody)}";

        foreach (var elseIf in node.ElseIfs)
        {
            text += '\n' + Indent($"else if ({Visit(elseIf.condition)}) {Visit(elseIf.body)}");
        }

        if (node.ElseBody != null)
        {
            text += '\n' + Indent($"else {Visit(node.ElseBody)}");
        }

        return text;
    }
    protected override string VisitFor(ForExpressionNode node)
    {
        return $"for ({Visit(node.Item)} in {Visit(node.Collection)}) {Visit(node.Body)}";
    }
    protected override string VisitMatch(MatchExpressionNode node)
    {
        var text = $"match ({Visit(node.Target)}) {{\n";

        Incr();
        text += string.Join("", node.Cases.Select(c => Indent(Visit(c))));
        Decr();

        text += '\n' + Indent("}");

        return text;
    }
    protected override string VisitCase(CaseExpressionNode node)
    {
        var text = Visit(node.Pattern);
        text += $" {Visit(node.Body)}";
        
        return text;
    }
    protected override string VisitLiteral(LiteralExpressionNode node)
    {
        return node.LiteralType switch
        {
            TokenType.IntLiteral when node.Value is int i => i.ToString(),
            TokenType.FloatLiteral when node.Value is float f => f.ToString(CultureInfo.CurrentCulture),
            TokenType.CharLiteral when node.Value is char c => $"'{c}'",
            TokenType.StringLiteral when node.Value is string s => $"\"{s}\"",
            TokenType.BooleanLiteral when node.Value is bool b => b ? "true" : "false",
            TokenType.NullLiteral => "null",
            TokenType.SelfLiteral => "self",
            TokenType.ItLiteral => "it",
            _ => node.Value.ToString() ?? throw new InvalidOperationException()
        };
    }
    protected override string VisitArrayLiteral(ArrayLiteralExpressionNode node)
    {
        return $"[ {string.Join(", ", node.Elements.Select(Visit))} ]";
    }
    protected override string VisitIdentifier(IdentifierExpressionNode node)
    {
        return node.Name;
    }
    protected override string VisitMemberAccess(MemberAccessExpressionNode node)
    {
        return $"{Visit(node.Target)}.{Visit(node.Member)}";
    }
    protected override string VisitCall(CallExpressionNode node)
    {
        bool isExpandedLambda = node.Arguments.LastOrDefault() is LambdaExpressionNode;
        if (isExpandedLambda)
        {
            var args = node.Arguments.SkipLast(1).ToArray();
            var text = $"{Visit(node.Target)}";
            if (args.Any()) text += $"({string.Join(", ", args.Select(Visit))})";
            text += $" {Visit(node.Arguments.Last())}";
            return text;
        }
        return $"{Visit(node.Target)}({string.Join(", ", node.Arguments.Select(Visit))})";
    }
    protected override string VisitIndex(IndexExpressionNode node)
    {
        return $"{Visit(node.Target)}[{Visit(node.Index)}]";
    }
    protected override string VisitRange(RangeExpressionNode node)
    {
        var text = "";
        if (node.Left is not null) text += Visit(node.Left);
        text += "..";
        if (node.Right is not null) text += Visit(node.Right);

        return text;
    }
    protected override string VisitBinary(BinaryExpressionNode node)
    {
        if (node.Operator is Operator.CastSymbol) return $"{Visit(node.Left)}{node.Operator.AsString()}{Visit(node.Right)}";
        return $"{Visit(node.Left)} {node.Operator.AsString()} {Visit(node.Right)}";
    }
    protected override string VisitUnary(UnaryExpressionNode node)
    {
        return node.IsPostFix 
            ? $"{Visit(node.Operand)}{node.Operator.AsString()}"
            : $"{node.Operator.AsString()}{Visit(node.Operand)}";
    }
    protected override string VisitLambda(LambdaExpressionNode node)
    {
        if (isLib) return "{ }";
        
        var text = "{ ";
        if (node.Parameters.Any())
        {
            text += string.Join(", ", node.Parameters.Select(Visit));
            text += " ->";
        }
        text += "\n";

        Incr();
        text += string.Join("", node.Body.Statements.Select(s => Indent(Visit(s))));
        Decr();

        text += "\n" + Indent("}");

        return text;
    }
    protected override string VisitInterpolation(StringInterpolationExpressionNode node)
    {
        var text = "$\"";

        foreach (var part in node.Parts)
        {
            if (part is LiteralExpressionNode str)
                text += Visit(str).Trim('"'); // remove the leading/trailing '"' from VisitLiteral
            else text += $"{{{Visit(part)}}}";
        }

        return text + '"';
    }
    protected override string VisitConcurrent(ConcurrentExpressionNode node)
    {
        return $"concurrent {Visit(node.Body)}";
    }
    protected override string VisitSpawn(SpawnExpressionNode node)
    {
        return $"spawn {Visit(node.Body)}";
    }
    protected override string VisitScoped(ScopedExpressionNode node)
    {
        return $"({Visit(node.Scoped)})";
    }

    #endregion
}