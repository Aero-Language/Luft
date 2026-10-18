using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.Printers;
using Luft.TypeChecker;
using Luft.Utility;

namespace Luft.Builder;

/// <param name="SingleOutput">Merge libraries into one output file instead of one dll per library</param>
/// <param name="OutputPath">Where the final file goes, defaults to the first project file's folder and name</param>
/// <param name="LinkerPath">A clang compatible driver that links with lld, defaults to 'clang' on PATH</param>
/// <param name="Package">Metadata of the library package, its id also names the init function; defaults to a local id</param>
public record AeroBuilderSettings(bool isExecutable = false, bool SingleOutput = false, string? OutputPath = null, string? LinkerPath = null, PackageMetadata? Package = null)
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
        // The binaries of library packages are unpacked here while the build runs
        var work = Path.Combine(Path.GetTempPath(), "aeroin-" + Guid.NewGuid().ToString("N"));

        try { return BuildCore(projectFiles, libFiles, settings, work); }
        finally
        {
            try { if (Directory.Exists(work)) Directory.Delete(work, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private string? BuildCore(string[] projectFiles, string[] libFiles, AeroBuilderSettings? settings, string work)
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
        List<LoadedLibrary> loaded = [];
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
        var reader = new LibraryReader(work) { Diagnostics = this.Diagnostics };
        foreach (var file in libFiles)
        {
            // A package, as opposed to a plain source file that is merged into the project
            if (file.EndsWith(".aerolib", StringComparison.OrdinalIgnoreCase))
            {
                if (reader.Read(file) is { } package) loaded.Add(package);
                continue;
            }

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

        var packages = reader.Order(loaded);
        if (packages is null) return null;

        // The headers stand in for the library's source, they are checked next to the project's own files
        List<(string Id, FileNode File)> headerFiles = [];
        foreach (var package in packages)
        {
            foreach (var (name, text) in package.Headers)
            {
                var tk = new Tokenizer { Diagnostics = this.Diagnostics };
                var ab = new AstBuilder { Diagnostics = this.Diagnostics };

                headerFiles.Add((package.Id, ab.BuildAst(tk.TokenizeSource(text, $"{package.Id}/{name}"))));
            }
        }
        if (Diagnostics.HasErrors) return null;

        var lookup = new TypeLookup { Diagnostics = this.Diagnostics };
        var typeResolver = new TypeResolver { Diagnostics = this.Diagnostics };
        var bodyResolver = new BodyResolver { Diagnostics = this.Diagnostics };
        
        var table = lookup.Run(files.ToArray(), libs.ToArray(), headerFiles);
        typeResolver.Run(table);
        bodyResolver.Run(table);

        // Never generate code from a program the checkers rejected
        if (Diagnostics.HasErrors) return null;

        var outputPath = Path.GetFullPath(settings.OutputPath ?? DefaultOutputPath(projectFiles[0], settings.isExecutable));
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        return settings.isExecutable 
            ? BuildExecutable(table, files, libs, packages, settings, outputPath) 
            : BuildLibrary(table, files, libs, packages, settings, outputPath);
    }

    private static string DefaultOutputPath(string firstFile, bool isExecutable)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(firstFile))!;
        var name = Path.GetFileNameWithoutExtension(firstFile);

        // A library is a package holding the shared library, the headers and a manifest
        var extension = isExecutable ? (OperatingSystem.IsWindows() ? ".exe" : "") : ".aerolib";

        return Path.Combine(dir, name + extension);
    }

    private string? BuildExecutable(TypeTable table, List<FileNode> files, List<ModuleDeclarationNode> libs, List<LoadedLibrary> packages, AeroBuilderSettings settings, string outputPath)
        => EmitAndLink(table, settings, outputPath, false, packages);

    // A `#lib` package: the shared library (no entry point, every public symbol exported, the init function and the C runtime inside),
    // the headers printed from the checked sources, and the manifest
    private string? BuildLibrary(TypeTable table, List<FileNode> files, List<ModuleDeclarationNode> libs, List<LoadedLibrary> packages, AeroBuilderSettings settings, string outputPath)
    {
        var package = settings.Package ?? new PackageMetadata("@local/" + new string(Path.GetFileNameWithoutExtension(outputPath).ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()));
        // The libraries this one was built against are listed as its dependencies unless the settings say otherwise
        if (package.Dependencies is null && packages.Count > 0)
            package = package with { Dependencies = packages.Select(p => new PackageDependency(p.Id, "^" + p.Version)).ToArray() };
        settings = settings with { Package = package };

        var work = Path.Combine(Path.GetTempPath(), "aerolib-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        try
        {
            var binary = Path.Combine(work, LibraryPackager.BinaryFileName(LibraryPackager.BinaryNameOf(package)));
            if (EmitAndLink(table, settings, binary, true, packages) is null) return null;

            // One header per source file, named after it
            var printer = new HeaderPrinter(table.Typed);
            var headers = new List<(string Name, string Text)>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file.Span.FilePath ?? "");
                if (name.Length == 0) name = "header";
                var unique = name;
                for (var i = 2; !used.Add(unique); i++) unique = $"{name}{i}";

                headers.Add(($"{unique}.aero", printer.PrintHeader(file)));
            }

            var modules = files.SelectMany(f => f.Modules).Select(m => m.ModulePath).Where(p => p.Length > 0).Distinct().ToList();

            var readmePath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(files[0].Span.FilePath ?? outputPath))!, "README.md");
            var readme = File.Exists(readmePath) ? File.ReadAllText(readmePath) : null;

            var luftVersion = typeof(AeroBuilder).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
            LibraryPackager.Write(outputPath, package, headers, modules, binary, readme, luftVersion);
            return outputPath;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Error($"Could not write the package: {e.Message}", SourceSpan.Unknown);
            return null;
        }
        finally
        {
            try { Directory.Delete(work, true); }
            catch (IOException) { }
        }
    }

    private string? EmitAndLink(TypeTable table, AeroBuilderSettings settings, string outputPath, bool isLibrary, IReadOnlyList<LoadedLibrary> packages)
    {
        using var generator = new CodeGenerator(Path.GetFileNameWithoutExtension(outputPath), table)
        {
            Diagnostics = this.Diagnostics,
            IsLibrary = isLibrary,
            LibraryId = settings.Package?.Id,
            LibraryInits = packages.Select(p => CodeGenerator.InitNameOf(p.Id)).ToList()
        };
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

            var extra = new List<string>();
            if (generator.UsesRuntime)
            {
                var runtime = Path.Combine(AppContext.BaseDirectory, "Runtime", "aero_runtime.c");
                if (!File.Exists(runtime))
                {
                    Error($"The runtime source '{runtime}' is missing.", SourceSpan.Unknown);
                    return null;
                }
                extra.Add(runtime);
            }

            // The runtime's symbols stay inside a library, so two libraries never clash
            var flags = new List<string>();
            if (isLibrary) flags.Add("-fvisibility=hidden");

            // An executable finds the libraries it links next to itself
            foreach (var package in packages)
            {
                var binary = package.BinaryPath;
                if (!isLibrary)
                {
                    binary = Path.Combine(Path.GetDirectoryName(outputPath)!, Path.GetFileName(package.BinaryPath));
                    File.Copy(package.BinaryPath, binary, true);
                }
                extra.Add(binary);
            }
            if (packages.Count > 0 && !OperatingSystem.IsWindows()) flags.Add(OperatingSystem.IsMacOS() ? "-Wl,-rpath,@loader_path" : "-Wl,-rpath,$ORIGIN");
            return linker.Link(objectPath, outputPath, isLibrary, settings.LinkerPath, extra, flags) ? outputPath : null;
        }
        finally
        {
            File.Delete(objectPath);
        }
    }
}