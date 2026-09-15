using Luft.Ast.Nodes;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.TypeChecker;

public sealed class TypeResolver : AeroThrower
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
        foreach (var fields in scope.Fields.Values) CheckFields(fields, scope);
        foreach (var properties in scope.Properties.Values) CheckProperties(properties, scope);
        foreach (var extensionProperties in scope.ExtensionProperties.Values) CheckExtensionProperties(extensionProperties, scope);
        foreach (var functions in scope.Functions.Values) CheckFunctions(functions, scope);
        foreach (var extensionFunctions in scope.ExtensionFunctions.Values) CheckExtensionFunctions(extensionFunctions, scope);
        foreach (var typeSymbol in scope.Types.Values) CheckTypeSymbol(typeSymbol);
    }

    private void CheckFields(List<FieldSymbol> fields, TypeScope scope)
    {
        var dupes = fields
            .GroupBy(p => p.Signature)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);
        
        foreach (var dupe in dupes) Error("A property with the same Signature was already declared.", dupe.Declaration.Span);
        foreach (var field in fields)
        {
            CheckType(field.Type, scope, field.Declaration.Span);
        }
    }
    private void CheckProperties(List<PropertySymbol> properties, TypeScope scope)
    {
        var dupes = properties
            .GroupBy(p => p.Signature)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);
        foreach (var dupe in dupes) Error("A property with the same Signature was already declared.", dupe.Declaration.Span);
        
        foreach (var property in properties.Where(p => p.ExtensionTarget is not null)) Error("Properties mustn't have a target Type.", property.Declaration.Span);
        foreach (var property in properties) CheckType(property.Type, scope, property.Declaration.Span);
    }
    private void CheckExtensionProperties(List<PropertySymbol> properties, TypeScope scope)
    {
        var dupes = properties
            .GroupBy(p => p.Signature)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);
        foreach (var dupe in dupes) Error("A property with the same Signature was already declared.", dupe.Declaration.Span);
        
        foreach (var property in properties.Where(p => p.ExtensionTarget is null)) Error("Extension properties must have a target Type.", property.Declaration.Span);
        foreach (var property in properties)
        {
            CheckType(property.Type, scope, property.Declaration.Span);
            CheckType(property.ExtensionTarget, scope, property.Declaration.Span);
        }
    }
    private void CheckFunctions(List<FunctionSymbol> functions, TypeScope scope)
    {
        var dupes = functions
            .GroupBy(p => p.Signature)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);
        foreach (var dupe in dupes) Error("A function with the same Signature was already declared.", dupe.Declaration.Span);
        
        foreach (var function in functions.Where(p => p.ExtensionTarget is not null)) Error("Functions mustn't have a target Type.", function.Declaration.Span);
        foreach (var function in functions)
        {
            var generics = function.GenericParameters.Select(g => g.Name).ToHashSet();
            
            foreach (var generic in function.GenericParameters) CheckType(generic, scope, function.Declaration.Span);
            CheckType(function.ReturnType, scope, function.Declaration.Span, generics);
            foreach (var param in function.Parameters) CheckType(param.Type, scope, function.Declaration.Span, generics);
        }
    }
    private void CheckExtensionFunctions(List<FunctionSymbol> functions, TypeScope scope)
    {
        var dupes = functions
            .GroupBy(p => p.Signature)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g);
        foreach (var dupe in dupes) Error("A function with the same Signature was already declared.", dupe.Declaration.Span);
        
        foreach (var function in functions.Where(p => p.ExtensionTarget is null)) Error("Extension functions must have a target Type.", function.Declaration.Span);
        foreach (var function in functions)
        {
            var generics = function.GenericParameters.Select(g => g.Name).ToHashSet();
            
            foreach (var generic in function.GenericParameters) CheckType(generic, scope, function.Declaration.Span);
            CheckType(function.ReturnType, scope, function.Declaration.Span, generics);
            foreach (var param in function.Parameters) CheckType(param.Type, scope, function.Declaration.Span, generics);
        }
    }
    private void CheckTypeSymbol(List<TypeSymbol> types)
    {
        foreach (var symbol in types)
        {
            CheckInheritance(symbol);
            
            switch (symbol)
            {
                case ClassSymbol c:
                    foreach (var g in c.GenericParameters) CheckType(g, symbol.Scope, c.Span);
                    break;
                case StructSymbol s:
                    foreach (var g in s.GenericParameters) CheckType(g, symbol.Scope, s.Span);
                    break;
                case TraitSymbol t:
                    foreach (var g in t.GenericParameters) CheckType(g, symbol.Scope, t.Span);
                    break;
                case RecordSymbol r:
                    foreach (var g in r.GenericParameters) CheckType(g, symbol.Scope, r.Span);
                    break;
                case AnnotationSymbol a:
                    foreach (var g in a.GenericParameters) CheckType(g, symbol.Scope, a.Span);
                    break;
                case EnumSymbol e:
                    foreach (var _ in e.GenericParameters) Error("Enums mustn't have generic parameters.", e.Span);
                    foreach (var p in e.Parameters) CheckType(p.Type, symbol.Scope, e.Span);
                    CheckType(e.MemberType, symbol.Scope, e.Span);
                    break;
            }
            
            CheckScope(symbol.Scope);
        }
    }


    #region Checkers

    private void CheckBody(BlockExpressionNode body, TypeScope scope, HashSet<string>? memberGenerics = null)
    {
        foreach (var statement in body.Statements)
        {
            // ToDo: Think about it
        }
    }
    private void CheckInheritance(TypeSymbol symbol)
    {
        switch (symbol)
        {
            case ClassSymbol c:
                var cImpls = c.Node.Implements.Select(i => CheckType(i, symbol.Scope, c.Span)).ToArray();
                if (cImpls.Count(p => p is ClassSymbol) > 1) Error("You can only have one base class", c.Span);
                
                for (int i = 0; i < cImpls.Length; i++)
                {
                    var impl = cImpls.ElementAt(i);
                    
                    if (impl is not null)
                    {
                        if (impl is ClassSymbol && i != 0) Error("Expected Trait", c.Span);
                    }
                    
                    if (impl is not TraitSymbol and not ClassSymbol and not null) Error("A class may only inherit from one base and multiple traits", c.Span);
                }
                break;
            case StructSymbol s:
                var sImpls = s.Node.Implements.Select(i => CheckType(i, symbol.Scope, s.Span)).ToArray();
                if (sImpls.Count(p => p is StructSymbol) > 1) Error("You can only have one base struct", s.Span);
                
                for (int i = 0; i < sImpls.Length; i++)
                {
                    var impl = sImpls.ElementAt(i);
                    
                    if (impl is not null)
                    {
                        if (impl is StructSymbol && i != 0) Error("Expected Trait", s.Span);
                    }
                    
                    if (impl is not TraitSymbol and not StructSymbol and not null) Error("A struct may only inherit from one base and multiple traits", s.Span);
                }
                break;
            case RecordSymbol r:
                var rImpls = r.Node.Implements.Select(i => CheckType(i, symbol.Scope, r.Span)).ToArray();
                if (rImpls.Count(p => p is StructSymbol) > 1) Error("You can only have one base record or struct", r.Span);
                
                for (int i = 0; i < rImpls.Length; i++)
                {
                    var impl = rImpls.ElementAt(i);
                    
                    if (impl is not null)
                    {
                        if (impl is RecordSymbol or StructSymbol && i != 0) Error("Expected Trait", r.Span);
                    }
                    
                    if (impl is not TraitSymbol and not RecordSymbol and not StructSymbol and not null) Error("A record may only inherit from one base and multiple traits", r.Span);
                }
                break;
            case TraitSymbol t:
                var traits = t.Node.Traits.Select(i => CheckType(i, symbol.Scope, t.Span)).ToArray();
                
                for (int i = 0; i < traits.Length; i++)
                {
                    var impl = traits.ElementAt(i);
                    
                    if (impl is not TraitSymbol and not null) Error("A trait may only inherit from other traits", t.Span);
                }
                break;
            
        }
    }
    private TypeSymbol? CheckType(AeroType? type, TypeScope scope, SourceSpan location, HashSet<string>? memberGenerics = null, AeroType? original = null)
    {
        switch (type)
        {
            case null or SpecialType: if (type == AeroType.Error) Error("Something went wrong in parsing!", location); return null;
            case ScalarType s:
                if (memberGenerics is not null)
                {
                    // Validate if the memberGenerics contains the type
                    if (memberGenerics.Contains(s.Name)) return null;
                }
                if (scope.AllGenerics().Contains(s.Name)) return null;
                
                if (TypeTable.PrimitiveTypes.Any(p => p.Name == s.Name)) return null; // If the type is a primitive, skip it, ignore ref/nullability
                
                // Try to get the type in the current scope
                if (!scope.Types.TryGetValue(s.Name, out var symbols))
                {
                    // Type was not found in the current scope
                    // Try to get the type from the parent scope
                    if (scope.ContainingScope != null)
                    {
                        return CheckType(type, scope.ContainingScope, location, original: original);
                    }
                    
                    // There is no parent scope, so try to get it from the file imports
                    if (Table.ImportsByFile.TryGetValue(location.FilePath, out var imports))
                    {
                        foreach (var import in imports)
                        {
                            if (Table.Modules.TryGetValue(import.TargetPath, out var importedModule))
                            {
                                if (importedModule.Scope.Types.TryGetValue(s.Name, out var foundSymbols))
                                {
                                    if (!foundSymbols.Select(t => t.Signature).Contains(original?.Signature ?? type.Signature)) Error("The type exists but there is no matching signature", location);
                                    return foundSymbols.FirstOrDefault(ts => ts.Signature.Equals(original?.Signature ?? type.Signature));
                                }
                            }
                        }
                    }
                        
                    Error($"Type '{type.Name}' could not be found in scope. Are you missing an import?", location);
                    return null;
                }
                
                if (!symbols.Select(t => t.Signature).Contains(original?.Signature ?? type.Signature)) Error("The type exists but there is no matching signature", location);
                return symbols.FirstOrDefault(ts => ts.Signature.Equals(original?.Signature ?? type.Signature));
            case ArrayType a: return CheckType(a.ElementType, scope, location);
            case GenericType g: foreach (var gp in g.TypeArguments) CheckType(gp, scope, location); return CheckType(g.Definition, scope, location, original: g);
            case GenericParameterType gp: CheckType(gp.Constraint, scope, location); return null;
            case LambdaType l: CheckType(l.ReturnType, scope, location); foreach (var p in l.Parameters) CheckType(p.Type, scope, location); return null;
            default: return null;
        }
    }
    #endregion
}