using Luft.TypeChecker.Symbols;

namespace Luft.TypeChecker;

public sealed class TypeScope(TypeScope? containing)
{
    public TypeScope? ContainingScope { get; } = containing;
    
    public Dictionary<string, List<TypeSymbol>> Types { get; } = new();

    public Dictionary<string, List<FunctionSymbol>> Functions { get; } = new();
    public Dictionary<string, List<PropertySymbol>> Properties { get; } = new();
    public Dictionary<string, List<FieldSymbol>> Fields { get; } = new();
    
    // ToDo: Extensions should be checked if their signature is already a member of the target
    public Dictionary<string, List<FunctionSymbol>> ExtensionFunctions { get; } = new();
    public Dictionary<string, List<PropertySymbol>> ExtensionProperties { get; } = new();
    
    public HashSet<string> GenericNames { get; init; } = new();
    
    public IEnumerable<string> AllGenerics()
    {
        var scope = this;
        while (scope != null)
        {
            foreach (var name in scope.GenericNames) yield return name;
            scope = scope.ContainingScope;
        }
    }
}