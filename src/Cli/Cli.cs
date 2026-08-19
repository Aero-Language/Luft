using ACLI;
using Luft.Builder;

namespace Luft.Cli;

public static class Cli
{
    static void Main(string[] args)
    {
        IEnumerable<SuperArgument> actions =
        [
            new(["b, build"], Build),
            new(["r", "run"], Run),
        ];

        var properties = new CliProperties(actions, new ConsoleStreams());
        var cli = new ACLI.Cli(properties);
        
        cli.Start(args);
    }

    static void Build(ACLI.Cli cli, PassedArg[] args)
    {
        var builder = new AeroBuilder();
    }
    
    static void Run(ACLI.Cli cli, PassedArg[] args)
    {
        
    }
}