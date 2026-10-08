using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker;
using Luft.Utility;

namespace Luft.Builder;

/// <param name="SingleOutput">Merge libraries into one output file instead of one dll per library</param>
/// <param name="OutputPath">Where the final file goes, defaults to the first project file's folder and name</param>
/// <param name="LinkerPath">A clang compatible driver that links with lld, defaults to 'clang' on PATH</param>
public record AeroBuilderSettings(bool isExecutable = false, bool SingleOutput = false, string? OutputPath = null, string? LinkerPath = null)
{
    public static readonly AeroBuilderSettings Default = new();
}

public class AeroBuilder : AeroThrower
{
    /// <summary>
    /// Builds the <paramref name="projectFiles"/> as a module
    /// </summary>
    /// <param name="projectFiles">An array of strings that contain all the file paths of a project</param>
    /// <param name="libFiles">An array of strings that contain all the file paths of the libraries of a project</param>
    /// <param name="options">The options for the build process</param>
    /// <param name="settings">Extra builder settings, defaults to separate dlls for libraries</param>
    /// <returns>The absolute path of the final executable or null if something failed</returns>
    public string? Build(string[] projectFiles, string[] libFiles, AeroBuilderSettings? settings = null)
    {
        Diagnostics ??= new DiagnosticBag();
        settings ??= AeroBuilderSettings.Default;

        if (projectFiles.Length == 0)
        {
            Error("No project files given.", SourceSpan.Unknown);
            return null;
        }
        
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

            // TODO: keep modules grouped per library file for separate dll output
            libs.AddRange(ast.Modules);
        }

        if (Diagnostics.HasErrors) return null;
        
        var lookup = new TypeLookup { Diagnostics = this.Diagnostics };
        var typeResolver = new TypeResolver { Diagnostics = this.Diagnostics };
        var bodyResolver = new BodyResolver { Diagnostics = this.Diagnostics };
        
        var table = lookup.Run(files.ToArray(), libs.ToArray());
        typeResolver.Run(table);
        bodyResolver.Run(table);

        // Never generate code from a program the checkers rejected
        if (Diagnostics.HasErrors) return null;

        var outputPath = Path.GetFullPath(settings.OutputPath ?? DefaultOutputPath(projectFiles[0], settings.isExecutable));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        return settings.isExecutable 
            ? BuildExecutable(table, files, libs, settings, outputPath) 
            : BuildLibrary(table, files, libs, settings, outputPath);
    }

    private static string DefaultOutputPath(string firstFile, bool isExecutable)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(firstFile))!;
        var name = Path.GetFileNameWithoutExtension(firstFile);

        var extension = isExecutable
            ? (OperatingSystem.IsWindows() ? ".exe" : "")
            : OperatingSystem.IsWindows() ? ".dll" : OperatingSystem.IsMacOS() ? ".dylib" : ".so";

        return Path.Combine(dir, name + extension);
    }

    private string? BuildExecutable(TypeTable table, List<FileNode> files, List<ModuleDeclarationNode> libs, AeroBuilderSettings settings, string outputPath)
    {
        using var generator = new CodeGenerator(Path.GetFileNameWithoutExtension(outputPath)) { Diagnostics = this.Diagnostics };
        if (!generator.Generate()) return null;

        var objectPath = outputPath + (OperatingSystem.IsWindows() ? ".obj" : ".o");
        if (!LlvmBackend.TryEmitObject(generator.Module, LlvmBackend.HostTriple, objectPath, out var error))
        {
            Error($"Could not emit the object file: {error}", SourceSpan.Unknown);
            return null;
        }

        try
        {
            var linker = new Linker { Diagnostics = this.Diagnostics };
            return linker.Link(objectPath, outputPath, false, settings.LinkerPath) ? outputPath : null;
        }
        finally
        {
            File.Delete(objectPath);
        }
    }
    
    private string? BuildLibrary(TypeTable table, List<FileNode> files, List<ModuleDeclarationNode> libs, AeroBuilderSettings settings, string outputPath)
    {
        throw new NotImplementedException();
    }
}