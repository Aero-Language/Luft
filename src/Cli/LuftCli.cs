using Acli;
using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker;
using Luft.Utility;

namespace Luft.Cli;

public static class LuftCli
{
    sealed record BuildFlag() : CommandFlag<BuildFlag>(["b", "build"], Build);
    sealed record RunFlag() : CommandFlag<BuildFlag>(["r", "run"], Run);

    private static readonly Flag[] Flags =
    [
        BuildFlag.Instance,
        RunFlag.Instance
    ];
    private static readonly CliProperties Properties = new(Flags);
    private static readonly Acli.Cli Cli = new(Properties);
    
    static void Main(string[] args)
    {
        Cli.Start(args);
    }

    static void Build(Flag[] flags, string[] values)
    {
        // Every stage reports into this one bag; nothing throws, so a single problem
        // can no longer hide the ones after it.
        var diagnostics = new DiagnosticBag();

        List<FileNode> files = [];
        foreach (var file in values)
        {
            if (!File.Exists(file))
            {
                Cli.Error($"File {file} not found. Terminating...");
                return;
            }
            
            var tk = new Tokenizer { Diagnostics = diagnostics };
            var ab = new AstBuilder { Diagnostics = diagnostics };
            
            var tokens = tk.Tokenize(file);
            var ast = ab.BuildAst(tokens);

            files.Add(ast);
        }

        // Don't type-check a tree that is known to be broken; report the syntax errors first.
        if (diagnostics.HasErrors)
        {
            PrintDiagnostics(diagnostics);
            return;
        }
        
        var lookup = new TypeLookup { Diagnostics = diagnostics };
        var typeResolver = new TypeResolver { Diagnostics = diagnostics };
        var bodyResolver = new BodyResolver { Diagnostics = diagnostics };
        
        var table = lookup.Run(files.ToArray(), []);
        typeResolver.Run(table);
        bodyResolver.Run(table);

        PrintDiagnostics(diagnostics);
    }
    
    static void PrintDiagnostics(DiagnosticBag diagnostics)
    {
        foreach (var diagnostic in diagnostics.Ordered())
        {
            Cli.ErrorLine(diagnostic.ToString());
            foreach (var related in diagnostic.Related) Cli.ErrorLine($"    note: {related.Message} ({related.Span})");
        }

        if (diagnostics.HasErrors) Cli.ErrorLine($"{diagnostics.ErrorCount} error(s).");
    }
    
    static void Run(Flag[] flags, string[] values)
    {
        
    }
}