using ACLI;
using Luft.AstBuilder.Ast;
using Luft.Builder;
using static Luft.Lexer.Lexer;
using static Luft.AstBuilder.Builder;

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
        
        // Temporary
        var first = args.First();
        if (first.Argument == "build")
        {
            foreach (var file in first.Values)
            {
                if (!File.Exists(file))
                {
                    cli.Error($"File {file} not found. Terminating...");
                    return;
                }
            }

            List<FileNode> files = [];
            foreach (var file in first.Values)
            {
                var tokens = Tokenize(file);
                // var ast = BuildAst(tokens);
                // files.Add(ast);
            }
        }
    }
    
    static void Run(ACLI.Cli cli, PassedArg[] args)
    {
        
    }
}