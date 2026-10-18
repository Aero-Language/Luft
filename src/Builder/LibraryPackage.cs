using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Luft.Builder;

public sealed record PackageDependency(string Id, string Version);

/// <param name="Id">The package id, lowercase and starting with '@', e.g. '@scope/name'</param>
public sealed record PackageMetadata(
    string Id,
    string? Name = null,
    string Version = "0.1.0",
    string[]? Authors = null,
    string? Description = null,
    string? Source = null,
    PackageDependency[]? Dependencies = null);

// Writes a `#lib` AeroPackage: manifest.json, the headers and the shared library of the host platform
public static class LibraryPackager
{
    public const int ManifestVersion = 1;

    // Raised on every change that breaks the binary interface, see the AeroLibrary specification
    public const int Abi = 1;

    public static string HostRid
    {
        get
        {
            var os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "arm64",
                Architecture.X86 => "x86",
                Architecture.Arm => "arm",
                _ => "x64"
            };
            return $"{os}-{arch}";
        }
    }

    // The file name of a shared library on this platform
    public static string BinaryFileName(string name)
        => OperatingSystem.IsWindows() ? $"{name}.dll" : OperatingSystem.IsMacOS() ? $"lib{name}.dylib" : $"lib{name}.so";

    // `@scope/name` becomes `name`, anything that is not a letter or digit becomes '_'
    public static string BinaryNameOf(PackageMetadata package)
    {
        var name = package.Id[(package.Id.LastIndexOf('/') + 1)..].TrimStart('@');
        return new string(name.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    }

    /// <param name="headers">Header file name (without folder) and its text</param>
    /// <param name="modules">Every module the headers declare</param>
    /// <param name="readme">The text of the README.md, null for none</param>
    public static void Write(string outputPath, PackageMetadata package, IReadOnlyList<(string Name, string Text)> headers,
        IReadOnlyList<string> modules, string binaryPath, string? readme, string luftVersion)
    {
        var rid = HostRid;
        var binaryEntry = $"package/bin/{rid}/{Path.GetFileName(binaryPath)}";

        if (File.Exists(outputPath)) File.Delete(outputPath);
        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);

        AddText(archive, "manifest.json", Manifest(package, modules, headers, binaryEntry, rid, luftVersion));
        if (readme is not null) AddText(archive, "README.md", readme);
        foreach (var (name, text) in headers) AddText(archive, $"package/headers/{name}", text);
        archive.CreateEntryFromFile(binaryPath, binaryEntry, CompressionLevel.Optimal);
    }

    private static string Manifest(PackageMetadata package, IReadOnlyList<string> modules, IReadOnlyList<(string Name, string Text)> headers,
        string binaryEntry, string rid, string luftVersion)
    {
        var meta = new JsonObject
        {
            ["luft_version"] = luftVersion,
            ["abi"] = Abi,
            ["modules"] = new JsonArray(modules.Select(m => (JsonNode)JsonValue.Create(m)!).ToArray()),
            ["headers"] = "package/headers",
            ["binaries"] = new JsonObject { [rid] = binaryEntry },
            ["dependencies"] = new JsonArray((package.Dependencies ?? []).Select(d => (JsonNode)new JsonObject { ["id"] = d.Id, ["version"] = d.Version }).ToArray())
        };

        var manifest = new JsonObject
        {
            ["manifest_version"] = ManifestVersion,
            ["package_type"] = "#lib",
            ["package_id"] = package.Id,
            ["name"] = package.Name ?? BinaryNameOf(package),
            ["version"] = package.Version,
            ["authors"] = new JsonArray((package.Authors ?? []).Select(a => (JsonNode)JsonValue.Create(a)!).ToArray()),
        };
        if (package.Description is not null) manifest["description"] = package.Description;
        if (package.Source is not null) manifest["source"] = package.Source;
        manifest["meta"] = meta;

        return manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void AddText(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var bytes = new UTF8Encoding(false).GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
    }
}
