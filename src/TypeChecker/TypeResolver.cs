using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.TypeChecker;

public sealed class TypeResolver : AeroThrower<SourceSpan>
{
    private TypeTable Table { get; set; } = null!;
    
    public void Run(TypeTable typeTable)
    {
        Table = typeTable;

        foreach (var module in Table.Modules.Values)
        {
            CheckScope(module.Scope, module);
        }
    }

    private void CheckType(AeroType? type, TypeScope scope, ModuleSymbol module, SourceSpan location)
    {
        switch (type)
        {
            case null or SpecialType: return; // Null, Auto or Error type
            case ScalarType s:
                // First: 
                
                if (TypeTable.PrimitiveTypes.Any(p => p.Name == s.Name)) return; // If the type is a primitive, skip it, ignore ref/nullability
                
                if (!module.Scope.Types.TryGetValue(s.Name, out _)) // Type not found locally
                {
                    bool wasFound = false;
                    if (Table.ImportsByFile.TryGetValue(module.Span.FilePath, out var imports))
                    {
                        foreach (var import in imports)
                        {
                            if (Table.Modules.TryGetValue(import.TargetPath, out var importedModule))
                            {
                                if (importedModule.Scope.Types.TryGetValue(s.Name, out _))
                                {
                                    // Type found in import
                                    wasFound = true;
                                }
                            }
                        }
                    }
                    
                    if (!wasFound) Error("Type could not be found in scope. Are you missing an import?", location);
                }
                break;
            case ArrayType a: CheckType(a.ElementType, scope, module, location); break;
            case GenericType g: CheckType(g.Definition, scope, module, location); foreach (var gp in g.TypeArguments) CheckType(gp, module, location); break;
            case GenericParameterType gp: CheckType(gp.Constraint, scope, module, location); break;
            case LambdaType l: CheckType(l.ReturnType, scope, module, location); foreach (var p in l.Parameters) CheckType(p.Type, module, location); break;
        }
    }
    private void CheckScope(TypeScope scope, ModuleSymbol module)
    {
        foreach (var fields in scope.Fields.Values) CheckFields(fields, module);
        foreach (var properties in scope.Properties.Values) CheckProperties(properties, module);
        foreach (var extensionProperties in scope.ExtensionProperties.Values) CheckExtensionProperties(extensionProperties, module);
        foreach (var functions in scope.Functions.Values) CheckFunctions(functions, module);
        foreach (var extensionFunctions in scope.ExtensionFunctions.Values) CheckExtensionFunctions(extensionFunctions, module);
        foreach (var typeSymbol in scope.Types.Values) CheckTypeSymbol(typeSymbol, module);
    }

    private void CheckFields(List<FieldSymbol> fields, ModuleSymbol module)
    {
        var dupes = fields
            .GroupBy(p => p.Signature)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);
        
        foreach (var dupe in dupes) Error("A property with the same Signature was already declared.", dupe.Declaration.Span);
        foreach (var field in fields)
        {
            CheckType(field.Type, module, field.Declaration.Span);
        }
    }
    private void CheckProperties(List<PropertySymbol> properties, ModuleSymbol module)
    {
        var dupes = properties
            .GroupBy(p => p.Signature)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);
        foreach (var dupe in dupes) Error("A property with the same Signature was already declared.", dupe.Declaration.Span);
        
        foreach (var property in properties.Where(p => p.ExtensionTarget is not null)) Error("Properties mustn't have a target Type.", property.Declaration.Span);
        foreach (var property in properties) CheckType(property.Type, module, property.Declaration.Span);
    }
    private void CheckExtensionProperties(List<PropertySymbol> properties, ModuleSymbol module)
    {
        var dupes = properties
            .GroupBy(p => p.Signature)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);
        foreach (var dupe in dupes) Error("A property with the same Signature was already declared.", dupe.Declaration.Span);
        
        foreach (var property in properties.Where(p => p.ExtensionTarget is null)) Error("Extension properties must have a target Type.", property.Declaration.Span);
        foreach (var property in properties)
        {
            CheckType(property.Type, module, property.Declaration.Span);
            CheckType(property.ExtensionTarget, module, property.Declaration.Span);
        }
    }
    private void CheckFunctions(List<FunctionSymbol> functions, ModuleSymbol module)
    {
        var dupes = functions
            .GroupBy(p => p.Signature)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);
        foreach (var dupe in dupes) Error("A function with the same Signature was already declared.", dupe.Declaration.Span);
        
        foreach (var function in functions.Where(p => p.ExtensionTarget is not null)) Error("Functions mustn't have a target Type.", function.Declaration.Span);
        foreach (var function in functions)
        {
            CheckType(function.ReturnType, module, function.Declaration.Span);
            foreach (var generic in function.GenericParameters) CheckType(generic, module, function.Declaration.Span);
            foreach (var param in function.Parameters) CheckType(param.Type, module, function.Declaration.Span);
        }
    }
    private void CheckExtensionFunctions(List<FunctionSymbol> functions, ModuleSymbol module)
    {
        var dupes = functions
            .GroupBy(p => p.Signature)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);
        foreach (var dupe in dupes) Error("A function with the same Signature was already declared.", dupe.Declaration.Span);
        
        foreach (var function in functions.Where(p => p.ExtensionTarget is null)) Error("Extension functions must have a target Type.", function.Declaration.Span);
        foreach (var function in functions)
        {
            CheckType(function.ReturnType, module, function.Declaration.Span);
            foreach (var generic in function.GenericParameters) CheckType(generic, module, function.Declaration.Span);
            foreach (var param in function.Parameters) CheckType(param.Type, module, function.Declaration.Span);
        }
    }
    private void CheckTypeSymbol(List<TypeSymbol> types, ModuleSymbol module)
    {
        foreach (var type in types)
        {
            switch (type)
            {
                case ClassSymbol c:
                    foreach (var i in c.Implements) CheckType(i, module, c.Span);
                    foreach (var g in c.GenericParameters) CheckType(g, module, c.Span);
                    break;
                case StructSymbol s:
                    foreach (var i in s.Implements) CheckType(i, module, s.Span);
                    foreach (var g in s.GenericParameters) CheckType(g, module, s.Span);
                    break;
                case TraitSymbol t:
                    foreach (var trait in t.Traits) CheckType(trait, module, t.Span);
                    foreach (var g in t.GenericParameters) CheckType(g, module, t.Span);
                    break;
                case RecordSymbol r:
                    foreach (var i in r.Implements) CheckType(i, module, r.Span);
                    foreach (var g in r.GenericParameters) CheckType(g, module, r.Span);
                    break;
                case AnnotationSymbol a:
                    foreach (var g in a.GenericParameters) CheckType(g, module, a.Span);
                    break;
                case EnumSymbol e:
                    foreach (var _ in e.GenericParameters) Error("Enums mustn't have generic parameters.", e.Span);
                    foreach (var p in e.Parameters) CheckType(p.Type, module, e.Span);
                    CheckType(e.MemberType, module, e.Span);
                    break;
            }
            
            CheckScope(type.Scope, module);
        }
    }
}