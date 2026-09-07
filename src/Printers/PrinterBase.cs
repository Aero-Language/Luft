using Luft.Ast.Nodes;
using Luft.Utility;

namespace Luft.Printers;

public abstract class PrinterBase(int indentAmount) : AstVisitor<string>
{
    private int _indent = 0;
    
    public string Print(FileNode node) => VisitFile(node);
    
    protected string Indent() => " ".Repeat(_indent * indentAmount);
    protected string Indent(string line) => Indent() + line;
    protected void Incr() => _indent++;
    protected void Decr() => _indent = Math.Max(0, _indent - 1); // Dont go below 0
    
    
    protected override string Default(AstNode ast)
    {
        return $"<Error> :: <{ast.GetType().Name}>";
    }
}