using Luft.Ast.Nodes;

namespace Luft.Printers;

public class TreePrinter(int indentAmount = 4) : PrinterBase(indentAmount)
{
    // Special
    protected override string VisitFile(FileNode node)
    {
        var output = Indent(node.Span.FilePath);
        
        Incr();
        foreach (var imports in node.Imports)
        {
            output += Visit(imports);
        }
        foreach (var module in node.Modules)
        {
            output += Visit(module);
        }
        Decr();
        
        return output;
    }
    protected override string VisitAnnotation(AnnotationStatementNode statementNode) => Indent($"@{statementNode.Name}");
    
    
    // Declarations
    protected override string VisitFunction(FunctionDeclarationNode node)
    {
        var output = "";
        foreach (var notation in node.Annotations) output += Visit(notation);

        var line = $"fun {node.Name}";
        if (node.GenericParameters != null) line += $"<{string.Join(", ", node.GenericParameters)}>";
        line += string.Join(", ", node.Parameters.Select(Visit));
        output += Indent(line);
        
        if (node.Body != null) output += Visit(node.Body);
        return output;
    }


    protected override string Default(AstNode ast)
        => ast switch
        {
            ParamNode p => $"{p.Name}: {p.Type}{(p.Initializer is not null ? $" = {Visit(p.Initializer)}" : "")}",
            _ => ast.GetType().Name
        };
}