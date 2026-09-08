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
            CheckScope(module.Scope);
        }
    }
    private void CheckScope(TypeScope scope)
    {
        foreach (var fields in scope.Fields.Values) CheckFields(fields);
        foreach (var properties in scope.Properties.Values) CheckProperties(properties);
        foreach (var functions in scope.Functions.Values) CheckFunctions(functions);
        foreach (var extensionProperties in scope.ExtensionProperties.Values) CheckExtensionProperties(extensionProperties);
        foreach (var extensionFunctions in scope.ExtensionFunctions.Values) CheckExtensionFunctions(extensionFunctions);
    }
    private void CheckTypes(TypeScope scope)
    {
        foreach (var (name, candidates) in scope.Types)
        {
            if (candidates.Count > 1)
            {
                // Types aren't overloadable — every extra declaration sharing this name is a conflict.
                foreach (var duplicate in candidates.Skip(1))
                    Error($"'{name}' is already declared in this scope.", duplicate.Span);
            }
 
            foreach (var type in candidates)
            {
                switch (type)
                {
                    case ClassSymbol c:
                        ResolveImplementsList(c, c.Implements.Select(at => (at, c.Span)).ToValueList());
                        break;
                    case StructSymbol s:
                        ResolveImplementsList(s, s.Implements.Select(at => (at, s.Span)).ToValueList());
                        break;
                    case RecordSymbol r:
                        ResolveImplementsList(r, r.Implements.Select(at => (at, r.Span)).ToValueList());
                        break;
                    case TraitSymbol t:
                        ResolveImplementsList(t, t.Traits.Select(at => (at, t.Span)).ToValueList());
                        break;
                    case EnumSymbol e:
                        CheckEnum(e);
                        break;
                    case AnnotationSymbol:
                        break; // nothing to resolve beyond the symbol itself yet
                }
 
                // Recurse into this type's own members (fields/properties/functions/nested types).
                CheckScope(type.Scope);
            }
        }
    }
    private void ResolveImplementsList(TypeSymbol owner, ValueList<(AeroType type, SourceSpan span)> rawEntries)
    {
        foreach (var raw in rawEntries)
        {
            
        } 
    }
    private void CheckEnum(EnumSymbol e)
    {
        if (e.IsEnumClass)
        {
            // enum class PlayerName(Name: String) { Steve = PlayerName("steve"), ... }
            // Each member's Value must be a call to the enum's own constructor.
            foreach (var param in e.Parameters)
            {
                // TODO: ResolveTypeRef(param.Type, e.Module.Scope) — constructor param types.
            }
 
            foreach (var member in e.Members)
            {
                if (member.Value is null)
                    Throw(e.Span, $"Enum class member '{member.Name}' of '{e.Name}' must be initialized with a constructor call.");
 
                // TODO (pass 3, once expression typing exists): check member.Value is a call
                // to e's own constructor with argument types matching e.Parameters.
            }
        }
        else
        {
            // enum GameState: Byte { Menu = 0, ... } — MemberType defaults to Int if omitted.
            // TODO: ResolveTypeRef(e.MemberType, e.Module.Scope) if MemberType is not null,
            // and confirm it resolves to an integral primitive.
 
            foreach (var member in e.Members)
            {
                // TODO (pass 3): if member.Value is present, it must be a constant expression
                // assignable to the enum's underlying MemberType.
            }
        }
    }



    
    private void CheckFields(List<FieldSymbol> fields)
    {
        
    }
    private void CheckProperties(List<PropertySymbol> properties)
    {
        
    }
    private void CheckFunctions(List<FunctionSymbol> functions)
    {
        
    }
    private void CheckExtensionProperties(List<PropertySymbol> properties)
    {
        
    }
    private void CheckExtensionFunctions(List<FunctionSymbol> functions)
    {
        
    }
}