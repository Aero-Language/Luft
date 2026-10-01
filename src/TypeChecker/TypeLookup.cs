using Luft.Ast.Nodes;
using Luft.TypeChecker.Symbols;
using Luft.Utility;

namespace Luft.TypeChecker;

public class TypeLookup : AstVisitor
{
    protected override CompilerStage Stage => CompilerStage.TypeLookup;
    
    private TypeTable Table { get; set; } = null!;
    private ModuleSymbol CurrentModule { get; set; } = null!;
    private TypeScope CurrentScope { get; set; } = null!;


    #region Special
    
    public TypeTable Run(FileNode[] ownFiles, ModuleDeclarationNode[] libModules)
    {
        Table = new();
        
        foreach (var file in ownFiles)
        {
            if (Table.ImportsByFile.TryGetValue(file.Span.FilePath, out var list)) list.AddRange(file.Imports);
            else Table.ImportsByFile.Add(file.Span.FilePath, file.Imports.ToList());
            
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
    // TKey is generic (rather than always 'string') so the same overload-collecting logic works
    // for both name-keyed tables (Functions, Properties, ...) and the Operator-keyed Operators table.
    private void AddOverloadable<TKey, TValue>(Dictionary<TKey, List<TValue>> dict, TKey key, TValue symbol) where TKey : notnull
    {
        if (dict.ContainsKey(key))
        {
            dict[key].Add(symbol);
        }
        else
        {
            dict.Add(key, [symbol]);
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
        var symbol = new ClassSymbol(node, CurrentModule, CurrentScope) { Scope = new TypeScope(CurrentScope) { GenericNames = node.GenericParameters.Select(g => g.Name).ToHashSet() } };

        foreach (var decl in node.Declarations)
        {
            AddDecl(symbol, decl);
        }

        AddType(node.Name, symbol);
    }
    protected override void VisitStruct(StructDeclarationNode node)
    {
        var symbol = new StructSymbol(node, CurrentModule, CurrentScope) { Scope = new TypeScope(CurrentScope) { GenericNames = node.GenericParameters.Select(g => g.Name).ToHashSet() } };

        foreach (var decl in node.Declarations)
        {
            AddDecl(symbol, decl);
        }

        AddType(node.Name, symbol);
    }
    protected override void VisitTrait(TraitDeclarationNode node)
    {
        var symbol = new TraitSymbol(node, CurrentModule, CurrentScope) { Scope = new TypeScope(CurrentScope) { GenericNames = node.GenericParameters.Select(g => g.Name).ToHashSet() } };

        foreach (var decl in node.Declarations)
        {
            AddDecl(symbol, decl);
        }

        AddType(node.Name, symbol);
    }
    protected override void VisitRecord(RecordDeclarationNode node)
    {
        var symbol = new RecordSymbol(node, CurrentModule, CurrentScope) { Scope = new TypeScope(CurrentScope) { GenericNames = node.GenericParameters.Select(g => g.Name).ToHashSet() } };

        foreach (var decl in node.Declarations)
        {
            AddDecl(symbol, decl);
        }

        AddType(node.Name, symbol);
    }
    protected override void VisitAnnotationDecl(AnnotationDeclarationNode node)
    {
        var symbol = new AnnotationSymbol(node, CurrentModule, CurrentScope) { Scope = new TypeScope(CurrentScope) { GenericNames = node.GenericParameters.Select(g => g.Name).ToHashSet() } };

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
    protected override void VisitOperator(OperatorDeclarationNode node)
    {
        AddOverloadable(CurrentScope.Operators, node.Op, new OperatorSymbol(node.Op, node.AccessMod, CurrentModule, node));
    }
    protected override void VisitProperty(PropertyDeclarationNode node)
    {
        AddOverloadable(CurrentScope.Properties, node.Name, new PropertySymbol(node.Name, node.AccessMod, node));
    }
    protected override void VisitField(FieldDeclarationNode node)
    {
        AddOverloadable(CurrentScope.Fields, node.Name, new FieldSymbol(node.Name, node.AccessMod, node));
    }
    protected override void VisitPrimaryConstructor(PrimaryConstructorDeclarationNode node)
    {
        CurrentScope.PrimaryConstructor = node;
        
        // `struct Vector2(var X: Float, var Y: Float)` declares its fields inline here rather
        // than in the body — without this override they never reach CurrentScope.Fields at all.
        foreach (var field in node.Variables) Visit(field);
    }
    protected override void VisitConstructor(ConstructorDeclarationNode node)
    {
        CurrentScope.Constructors.Add(new ConstructorSymbol(node.AccessMod, node));
    }

    #endregion
    
}