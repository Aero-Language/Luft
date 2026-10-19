using System.Diagnostics;
using Acli;
using Luft.Builder;
using Luft.Utility;

namespace Luft.Cli;

public static class LuftCli
{
    sealed record BuildFlag() : CommandFlag<BuildFlag>(["b", "build"], Build);
    sealed record RunFlag() : CommandFlag<RunFlag>(["r", "run"], Run);
    sealed record SingleFlag() : Flag<SingleFlag>(["single"]);
    sealed record EmitIrFlag() : Flag<EmitIrFlag>(["emit-ir"]);
    sealed record LibraryFlag() : Flag<LibraryFlag>(["library"]);

    private static readonly Flag[] Flags =
    [
        BuildFlag.Instance,
        RunFlag.Instance,
        SingleFlag.Instance,
        EmitIrFlag.Instance,
        LibraryFlag.Instance
    ];
    private static readonly CliProperties Properties = new(Flags);
    private static readonly Acli.Cli Cli = new(Properties);

    // Acli only knows switches, so options with a value are written `--name=value` and arrive among the values
    private sealed record Options(
        string[] Files, string[] Libs, string[] ProgramArgs,
        string? Output, string? Linker, string? PackageId, string? PackageVersion,
        bool Single, bool EmitIr, bool Library);

    static void Main(string[] args)
    {
        Cli.Start(args);
    }

    private static bool TryOption(string text, string name, out string value)
    {
        var prefix = $"--{name}=";
        var matches = text.StartsWith(prefix, StringComparison.Ordinal);
        value = matches ? text[prefix.Length..] : "";
        return matches;
    }

    // Arguments after a lone `--` belong to the program that `-r` starts, `.aerolib` files are libraries wherever they stand
    private static Options? Parse(Flag[] flags, string[] values)
    {
        List<string> files = [], libs = [], programArgs = [];
        string? output = null, linker = null, packageId = null, packageVersion = null;
        var rest = false;

        foreach (var value in values)
        {
            if (rest) programArgs.Add(value);
            else if (value == "--") rest = true;
            else if (TryOption(value, "output", out var o)) output = o;
            else if (TryOption(value, "linker", out var l)) linker = l;
            else if (TryOption(value, "lib", out var lib)) libs.Add(lib);
            else if (TryOption(value, "package-id", out var id)) packageId = id;
            else if (TryOption(value, "package-version", out var version)) packageVersion = version;
            else if (value.StartsWith('-'))
            {
                Cli.ErrorLine($"Unknown option '{value}'.");
                return null;
            }
            else if (value.EndsWith(".aerolib", StringComparison.OrdinalIgnoreCase)) libs.Add(value);
            else files.Add(value);
        }

        if (files.Count == 0)
        {
            Cli.ErrorLine("No source files given.");
            return null;
        }

        return new Options(files.ToArray(), libs.ToArray(), programArgs.ToArray(), output, linker, packageId, packageVersion,
            flags.Contains(SingleFlag.Instance), flags.Contains(EmitIrFlag.Instance), flags.Contains(LibraryFlag.Instance));
    }

    // Builds the program or, with --library, the package; null when something failed, the diagnostics are printed either way
    private static string? BuildFrom(Flag[] flags, string[] values, bool forRun)
    {
        if (Parse(flags, values) is not { } options)
        {
            Environment.ExitCode = 2;
            return null;
        }

        if (forRun && options.Library)
        {
            Cli.ErrorLine("A library cannot be run.");
            Environment.ExitCode = 2;
            return null;
        }

        var package = options.PackageId is null ? null : new PackageMetadata(options.PackageId, Version: options.PackageVersion ?? "0.1.0");
        var settings = new AeroBuilderSettings(!options.Library, options.Single, options.Output, options.Linker, package, options.EmitIr);

        var builder = new AeroBuilder();
        var result = builder.Build(options.Files, options.Libs, settings);

        if (builder.Diagnostics is { Count: > 0 }) PrintDiagnostics(builder.Diagnostics);
        if (result is null || builder.Diagnostics is { HasErrors: true })
        {
            Environment.ExitCode = 1;
            return null;
        }

        return result;
    }

    static void Build(Flag[] flags, string[] values)
    {
        // `-b -r` builds once, in the run command
        if (flags.Contains(RunFlag.Instance)) return;

        if (BuildFrom(flags, values, false) is { } path) Cli.LogLine($"{(flags.Contains(LibraryFlag.Instance) ? "Library" : "Binary")} Path: {path}");
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

    // Builds, starts the program with the arguments after `--` and passes its exit code on
    static void Run(Flag[] flags, string[] values)
    {
        if (BuildFrom(flags, values, true) is not { } exec) return;
        var options = Parse(flags, values)!;

        var info = new ProcessStartInfo(exec) { UseShellExecute = false };
        foreach (var argument in options.ProgramArgs) info.ArgumentList.Add(argument);

        try
        {
            using var process = Process.Start(info)!;
            process.WaitForExit();
            Environment.ExitCode = process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception e)
        {
            Cli.ErrorLine($"Could not start '{exec}': {e.Message}");
            Environment.ExitCode = 1;
        }
    }
}
