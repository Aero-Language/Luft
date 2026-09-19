namespace Luft.TypeChecker.Symbols;

public class BodyScope(BodyScope? parent = null)
{
    public BodyScope? Parent { get; } = parent;

    public Dictionary<string, VariableSymbol> Variables { get; } = [];

    public bool TryAdd(VariableSymbol variable)
    {
        if (Get(variable.Name) is null) //No variable with the name
        {
            Variables.Add(variable.Name, variable);
            return true;
        }

        return false;
    }

    public VariableSymbol? Get(string name)
    {
        var scope = this;
        VariableSymbol? result = null;
        while (scope is not null)
        {
            if (scope.Variables.TryGetValue(name, out var variable)) result = variable;
            scope = scope.Parent;
        }
        
        return result;
    }

    public void Set(string name, VariableSymbol variable)
    {
        var scope = this;
        while (scope is not null)
        {
            if (scope.Variables.ContainsKey(name)) scope.Variables[name] = variable;
            scope = scope.Parent;
        }
    }
}