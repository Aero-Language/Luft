using Luft.TypeChecker.Symbols;

namespace Luft.TypeChecker;

public sealed class TypeScope(TypeScope? containing)
{
    public TypeScope? ContainingScope { get; } = containing;
    
    public Dictionary<string, List<TypeSymbol>> Types { get; } = new();

    public Dictionary<string, List<FunctionSymbol>> Functions { get; } = new();
    public Dictionary<string, List<PropertySymbol>> Properties { get; } = new();
    public Dictionary<string, List<FieldSymbol>> Fields { get; } = new();
    
    public Dictionary<string, List<FunctionSymbol>> ExtensionFunctions { get; } = new();
    public Dictionary<string, List<PropertySymbol>> ExtensionProperties { get; } = new();
    
    
}