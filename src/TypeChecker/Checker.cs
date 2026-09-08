using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.TypeChecker;

public sealed class Checker : AeroThrower<SourceSpan>
{
    private TypeTable Table { get; set; } = null!;
    
    public void Run(TypeTable typeTable)
    {
        Table = typeTable;

        foreach (var module in Table.Modules.Values)
        {
            CheckScope(module.Scope);
        }
    }

    private void CheckScope(TypeScope scope)
    {
        foreach (var fields in scope.Fields.Values) CheckFields(fields);
        foreach (var properties in scope.Properties.Values) CheckProperties(properties);
        foreach (var extensionProperties in scope.ExtensionProperties.Values) CheckExtensionProperties(extensionProperties);
        
    }

    private void CheckFields(List<FieldSymbol> fields)
    {
        if (fields.Count > 1)
            foreach (var field in fields) 
                Error("A field with this Identifier has already been declared.", field.Declaration.Span);
    }

    private void CheckProperties(List<PropertySymbol> properties)
    {
        if (properties.Count > 1)
            foreach (var property in properties)
                Error("A property with this Identifier has already been declared.", property.Declaration.Span);
    }

    private void CheckExtensionProperties(List<PropertySymbol> properties)
    {
        var dupes = properties
            .GroupBy(x => x.ExtensionTarget)
            .Where(group => group.Select(x => x.Name).Distinct().Count() < group.Count())
            .Select(group =>
                group.GroupBy(x => x.Name)               // This is the detection function
                    .Where(g => g.Count() > 1) // 
                    .SelectMany(g => g)
                    .ToList()
            ).ToList();


        foreach (var dupesByType in dupes)
        {
            foreach (var dupe in dupesByType)
            {
                Error("An extension property with this Identifier has already been declared.", dupe.Declaration.Span);
            }
        }
    }
}