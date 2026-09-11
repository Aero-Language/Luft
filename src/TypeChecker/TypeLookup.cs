using Luft.Ast.Nodes;
using Luft.TypeChecker.Symbols;

namespace Luft.TypeChecker;

public class TypeLookup : AstVisitor
{
    private TypeTable Table { get; set; } = null!;
    private ModuleSymbol CurrentModule { get; set; } = null!;
    private TypeScope CurrentScope { get; set; } = null!;


    #region Special
    
    public TypeTable Run(FileNode[] ownFiles, ModuleDeclarationNode[] libModules)
    {
        Table = new();
        
        foreach (var file in ownFiles)
        {
            foreach (var module in file.Modules) Visit(module);
        }
        
        foreach (var module in libModules) Visit(module);
        
        return Table;
    }
    protected override void VisitModule(ModuleDeclarationNode node)
    {
        CurrentModule = Table.GetOrAddModule(node.ModulePath, node.Span);
        CurrentScope = CurrentModule.Scope;
        
        foreach (var decl in node.Declarations)
        {
            Visit(decl);
        }
    }
    private void AddOverloadable<TValue>(Dictionary<string, List<TValue>> dict, string name, TValue symbol)
    {
        if (dict.ContainsKey(name))
        {
            dict[name].Add(symbol);
        }
        else
        {
            dict.Add(name, [symbol]);
        }
    }
    private void AddType(string name, TypeSymbol symbol) => AddOverloadable(CurrentScope.Types, name, symbol);
    private void AddDecl(TypeSymbol symbol, DeclarationNode node)
    {
        var oldScope = CurrentScope;
        CurrentScope = symbol.Scope;

        Visit(node);
        
        CurrentScope = oldScope;
    }

    #endregion
    
    #region Declarations

    protected override void VisitClass(ClassDeclarationNode node)
    {
        var symbol = new ClassSymbol(node, CurrentModule, CurrentScope) { Scope = new TypeScope(CurrentScope) };

        foreach (var decl in node.Declarations)
        {
            AddDecl(symbol, decl);
        }

        AddType(node.Name, symbol);
    }
    protected override void VisitStruct(StructDeclarationNode node)
    {
        var symbol = new StructSymbol(node, CurrentModule, CurrentScope) { Scope = new TypeScope(CurrentScope) };

        foreach (var decl in node.Declarations)
        {
            AddDecl(symbol, decl);
        }

        AddType(node.Name, symbol);
    }
    protected override void VisitTrait(TraitDeclarationNode node)
    {
        var symbol = new TraitSymbol(node, CurrentModule, CurrentScope) { Scope = new TypeScope(CurrentScope) };

        foreach (var decl in node.Declarations)
        {
            AddDecl(symbol, decl);
        }

        AddType(node.Name, symbol);
    }
    protected override void VisitRecord(RecordDeclarationNode node)
    {
        var symbol = new RecordSymbol(node, CurrentModule, CurrentScope) { Scope = new TypeScope(CurrentScope) };

        foreach (var decl in node.Declarations)
        {
            AddDecl(symbol, decl);
        }

        AddType(node.Name, symbol);
    }
    protected override void VisitAnnotationDecl(AnnotationDeclarationNode node)
    {
        var symbol = new AnnotationSymbol(node, CurrentModule, CurrentScope) { Scope = new TypeScope(CurrentScope) };

        foreach (var decl in node.Declarations)
        {
            AddDecl(symbol, decl);
        }

        AddType(node.Name, symbol);
    }
    protected override void VisitEnum(EnumDeclarationNode node)
    {
        AddType(node.Name, new EnumSymbol(node, CurrentModule, CurrentScope)
        {
            Scope = new TypeScope(CurrentScope)
        });
    }
    protected override void VisitExtension(ExtensionDeclarationNode node)
    {
        if (node.Extension is FunctionDeclarationNode fun) AddOverloadable(CurrentScope.ExtensionFunctions, fun.Name, new FunctionSymbol(fun.Name, fun.AccessMod, CurrentModule, fun) { ExtensionTarget = node.TargetType });
        else if (node.Extension is PropertyDeclarationNode prop) AddOverloadable(CurrentScope.ExtensionProperties, prop.Name, new PropertySymbol(prop.Name, prop.AccessMod, prop) { ExtensionTarget = node.TargetType });
    }
    protected override void VisitExtensionBlock(ExtensionBlockDeclarationNode node)
    {
        foreach (var ext in node.Extensions)
        {
            Visit(ext);
        }
    }
    protected override void VisitFunction(FunctionDeclarationNode node)
    {
        AddOverloadable(CurrentScope.Functions, node.Name, new FunctionSymbol(node.Name, node.AccessMod, CurrentModule, node));
    }
    protected override void VisitProperty(PropertyDeclarationNode node)
    {
        AddOverloadable(CurrentScope.Properties, node.Name, new PropertySymbol(node.Name, node.AccessMod, node));
    }
    protected override void VisitField(FieldDeclarationNode node)
    {
        AddOverloadable(CurrentScope.Fields, node.Name, new FieldSymbol(node.Name, node.AccessMod, node));
    }

    #endregion
    
}