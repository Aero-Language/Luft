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
        // * Temporary *
        Action<Exception> error(string stage) => (Exception e) =>
        {
#if DEBUG
            throw e;
#endif
            Cli.ErrorLine($"{stage}: " + e.Message);
        };

        
        List<FileNode> files = [];
        foreach (var file in values)
        {
            if (!File.Exists(file))
            {
                Cli.Error($"File {file} not found. Terminating...");
                return;
            }
            
            var tk = new Tokenizer();
            var ab = new AstBuilder();

            tk.OnError += error("Lexer");
            ab.OnError += error("Ast");
            
            var tokens = tk.Tokenize(file);
            var ast = ab.BuildAst(tokens);

            files.Add(ast);
        }
        
        var lookup = new TypeLookup();
        var typeResolver = new TypeResolver();
        var bodyResolver = new BodyResolver();
        lookup.OnError += error("TypeLookup");
        typeResolver.OnError += error("TypeChecker");
        bodyResolver.OnError += error("BodyChecker");
        
        var table = lookup.Run(files.ToArray(), []);
        typeResolver.Run(table);
        bodyResolver.Run(table);
    }
    
    static void Run(Flag[] flags, string[] values)
    {
        
    }
}