using System.Diagnostics;
using Acli;
using Luft.Builder;
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
        var builder = new AeroBuilder();
        builder.Build(values, [], new(isExecutable: true));
        
        if (builder.Diagnostics is { HasErrors: true })
        {
            PrintDiagnostics(builder.Diagnostics);
        }
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
        var builder = new AeroBuilder();
        var exec = builder.Build(values, [], new(isExecutable: true));
        
        if (builder.Diagnostics is { HasErrors: true })
        {
            PrintDiagnostics(builder.Diagnostics);
        }
        else
        {
            var info = new ProcessStartInfo()
            {
                FileName = exec
            };
            using var process = Process.Start(info)!;
            process.WaitForExit();
        }
    }
}