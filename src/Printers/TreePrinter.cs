using System.Text;
using Luft.Ast.Nodes;
using Luft.Utility;

namespace Printers;

public class TreePrinter(int indentAmount = 4) : AstVisitor<string>
{
    private StringBuilder _builder = new ();
    private int _indent = 0;
    
    public string Print(FileNode node)
    {
        _builder = new StringBuilder();
        Visit(node);
        return _builder.ToString();
    }

    private string Line(string line) => Enumerable.Repeat("", _indent * indentAmount) + line;
    private void Incr() => _indent++;
    private void Decr() => _indent = Math.Max(0, _indent - 1); // Dont go below 0
    
    
    // Special
    protected override string VisitFile(FileNode node)
    {
        var output = Line(node.Span.FilePath);
        
        Incr();
        foreach (var imports in node.Imports)
        {
            output += Visit(imports);
        }
        foreach (var global in node.Globals)
        {
            output += Visit(global);
        }
        foreach (var module in node.Modules)
        {
            output += Visit(module);
        }
        Decr();
        
        return output;
    }
    protected override string VisitAnnotation(AnnotationNode node) => Line($"@{node.Name}");
    
    
    // Declarations
    protected override string VisitFunction(FunctionDeclarationNode node)
    {
        var output = "";
        foreach (var notation in node.Annotations) output += Visit(notation);
        
        var line = $"fun {node.Name}";
        if (node.GenericParameters != null) line += $"<{string.Join(", ", node.GenericParameters)}>";
        
        output += Line(line);
        if (node.Body != null) output += Visit(node.Body);
        return output;
    }

    
    protected override string Default(AstNode ast)
    {
        return ast.GetType().Name;
    }
}