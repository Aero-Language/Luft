using Luft.Ast.Nodes;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.TypeChecker;

public sealed class TypeScope(TypeScope? containing)
{
    public TypeScope? ContainingScope { get; } = containing;
    
    public Dictionary<string, List<TypeSymbol>> Types { get; } = new();

    public Dictionary<string, List<FunctionSymbol>> Functions { get; } = new();
    public Dictionary<string, List<PropertySymbol>> Properties { get; } = new();
    public Dictionary<string, List<FieldSymbol>> Fields { get; } = new();
    
    // Explicit `constructor` declarations, plus the inline `Type(var X: Int)` form if present
    public List<ConstructorSymbol> Constructors { get; } = [];
    public PrimaryConstructorDeclarationNode? PrimaryConstructor { get; set; }
    
    // ToDo: Extensions should be checked if their signature is already a member of the target
    public Dictionary<string, List<FunctionSymbol>> ExtensionFunctions { get; } = new();
    public Dictionary<string, List<PropertySymbol>> ExtensionProperties { get; } = new();

    // Operator overloads declared directly on this type (`operator fun Add(...)` etc.), keyed
    // by the Operator they overload rather than by name — operators don't have one. Multiple
    // overloads of the same operator (different arity/parameter types) share a key, same as
    // overloaded Functions share a name.
    public Dictionary<Operator, List<OperatorSymbol>> Operators { get; } = new();
    
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