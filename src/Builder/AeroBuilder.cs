using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker;
using Luft.Utility;

namespace Luft.Builder;

public record BuildOptions(bool isExecutable)
{
    public static readonly BuildOptions Default = new BuildOptions(true);
}

public class AeroBuilder : AeroThrower
{
    /// <summary>
    /// Builds the <paramref name="projectFiles"/> as a module
    /// </summary>
    /// <param name="projectFiles">An array of strings that contain all the file paths of a project</param>
    /// <param name="libFiles">An array of strings that contain all the file paths of the libraries of a project</param>
    /// <param name="options">The options for the build process</param>
    /// <returns>The absolute path of the final executable or null if something failed</returns>
    public string? Build(string[] projectFiles, string[] libFiles, BuildOptions options)
    {
        List<FileNode> files = [];
        List<ModuleDeclarationNode> libs = [];
        foreach (var file in projectFiles)
        {
            if (!File.Exists(file))
            {
                Error("File not found.", SourceSpan.Unknown);
                return null;
            }
            
            var tk = new Tokenizer { Diagnostics = this.Diagnostics };
            var ab = new AstBuilder { Diagnostics = this.Diagnostics };
            
            var tokens = tk.Tokenize(file);
            var ast = ab.BuildAst(tokens);

            files.Add(ast);
        }
        foreach (var file in libFiles)
        {
            if (!File.Exists(file))
            {
                Error("Library not found.", SourceSpan.Unknown);
                return null;
            }
            
            var tk = new Tokenizer { Diagnostics = this.Diagnostics };
            var ab = new AstBuilder { Diagnostics = this.Diagnostics };
            
            var tokens = tk.Tokenize(file);
            var ast = ab.BuildAst(tokens);

            libs.AddRange(ast.Modules);
        }

        if (this.Diagnostics is { HasErrors: true }) return "";
        
        var lookup = new TypeLookup { Diagnostics = this.Diagnostics };
        var typeResolver = new TypeResolver { Diagnostics = this.Diagnostics };
        var bodyResolver = new BodyResolver { Diagnostics = this.Diagnostics };
        
        var table = lookup.Run(files.ToArray(), libs.ToArray());
        typeResolver.Run(table);
        bodyResolver.Run(table);

        return options.isExecutable 
            ? BuildExecutable(files, libs) 
            : BuildLibrary(files, libs);
    }

    private string BuildExecutable(List<FileNode> files, List<ModuleDeclarationNode> libs)
    {
        
    }
    
    private string BuildLibrary(List<FileNode> files, List<ModuleDeclarationNode> libs)
    {
        
    }
}