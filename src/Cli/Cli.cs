using ACLI;
using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Builder;
using Luft.Lexer;

namespace Luft.Cli;

public static class Cli
{
    static void Main(string[] args)
    {
        IEnumerable<SuperArgument> actions =
        [
            new(["b", "build"], Build),
            new(["r", "run"], Run),
        ];

        var properties = new CliProperties(actions, ConsoleStreams.Default);
        var cli = new ACLI.Cli(properties);
        
        cli.Start(args);
    }

    static void Build(ACLI.Cli cli, PassedArg[] args)
    {
        var builder = new AeroBuilder();
        // ToDo: Implement after builder is done
        
        // * Temporary *
        var first = args.First();
        List<FileNode> files = [];
        foreach (var file in first.Values)
        {
            if (!File.Exists(file))
            {
                cli.Error($"File {file} not found. Terminating...");
                return;
            }

            var tk = new Tokenizer();
            var ab = new AstBuilder();
            
            ab.OnError += e => cli.Error(e.Message);
            
            var tokens = tk.Tokenize(file);
            var ast = ab.BuildAst(tokens);
            files.Add(ast);
        }
    }
    
    static void Run(ACLI.Cli cli, PassedArg[] args)
    {
        
    }
}