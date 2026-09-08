using Acli;
using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker;

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
        // var builder = new AeroBuilder();
        // ToDo: Implement after compiler
        
        
        // * Temporary *
        List<FileNode> files = [];
        foreach (var file in values)
        {
            if (!File.Exists(file))
            {
                Cli.Error($"File {file} not found. Terminating...");
                return;
            }

            var error = (Exception e) =>
            {
                #if DEBUG
                throw e;
                #endif
                Cli.ErrorLine(e.Message);
            };

            var tk = new Tokenizer();
            var ab = new AstBuilder();

            tk.OnError += error;
            ab.OnError += error;
            
            var tokens = tk.Tokenize(file);
            var ast = ab.BuildAst(tokens);

            files.Add(ast);
        }
        
        var lookup = new Lookup();
        var checker = new Checker();
        var table = lookup.Run(files.ToArray(), []);
        checker.Run(table);
    }
    
    static void Run(Flag[] flags, string[] values)
    {
        
    }
}