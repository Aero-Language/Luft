using System.Diagnostics;
using LLVMSharp.Interop;
using Luft.Utility;

namespace Luft.Builder;

// Runs clang with -fuse-ld=lld; neither LLVMSharp nor libLLVM ship a linker
public sealed class Linker : AeroThrower
{
    protected override CompilerStage Stage => CompilerStage.Linker;

    public bool Link(string objectPath, string outputPath, bool isLibrary, string? driverPath = null)
    {
        var driver = driverPath ?? FindOnPath("clang");
        if (driver is null)
        {
            Error("Could not find 'clang' on PATH. Install clang or set AeroBuilderSettings.LinkerPath.", SourceSpan.Unknown);
            return false;
        }

        var info = new ProcessStartInfo(driver)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        info.ArgumentList.Add("-fuse-ld=lld");
        if (isLibrary) info.ArgumentList.Add("-shared");
        info.ArgumentList.Add(objectPath);
        info.ArgumentList.Add("-o");
        info.ArgumentList.Add(outputPath);

        try
        {
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            process.WaitForExit();

            if (process.ExitCode == 0) return true;

            Error($"Linking failed ({Path.GetFileName(driver)} exit code {process.ExitCode}): {stderr.Result}{stdout.Result}".Trim(), SourceSpan.Unknown);
            return false;
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or IOException)
        {
            Error($"Could not start the linker '{driver}': {e.Message}", SourceSpan.Unknown);
            return false;
        }
    }

    private static string? FindOnPath(string name)
    {
        var file = OperatingSystem.IsWindows() ? name + ".exe" : name;
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";

        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, file);
            var fileInfo = new FileInfo(candidate);

            if (fileInfo.Exists)
            {
                if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) return fileInfo.LinkTarget;
                else return candidate;
            }
        }

        return null;
    }
}