using Luft.Ast.Nodes;
using Luft.TypeChecker.Symbols;

namespace Luft.TypeChecker;

public sealed class Checker : AstVisitor
{
    private TypeTable Table { get; set; } = null!;
    private List<ModuleSymbol> AvailableModules { get; } = [];
    
    public void Run(FileNode node, TypeTable typeTable)
    {
        Table = typeTable;
        AvailableModules.Clear();
        
        Visit(node);
    }


    #region Special

    protected override void VisitFile(FileNode node)
    {
        foreach (var import in node.Imports)
        {
            if (Table.Modules.TryGetValue(import.TargetPath, out var module))
            {
                AvailableModules.Add(module);
            }
            else
            {
                Error("Unable to find module.", import.Span);
            }
        }

        // Also add all the modules in the file to the available
        AvailableModules.AddRange(Table.Modules.Values.Where(s => node.Modules.Select(mod => mod.ModulePath).Contains(s.ModulePath)));

        foreach (var module in node.Modules)
        {
            Visit(module);
        }
    }

    #endregion
    
    #region Expressions
    
    
    
    #endregion
}