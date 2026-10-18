using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Luft.Utility;

namespace Luft.Builder;

/// <param name="Id">The package id from the manifest</param>
/// <param name="Headers">Header file name and its text</param>
/// <param name="BinaryPath">The shared library of this platform, extracted into the reader's work folder</param>
/// <param name="Dependencies">Ids of the packages this one calls</param>
public sealed record LoadedLibrary(string Id, string Version, string[] Modules, IReadOnlyList<(string Name, string Text)> Headers,
    string BinaryPath, string[] Dependencies, string PackagePath);

// Opens `#lib` packages: checks the manifest, reads the headers and extracts the binary of the host platform
public sealed class LibraryReader(string workDirectory) : AeroThrower
{
    protected override CompilerStage Stage => CompilerStage.Package;

    private int _count;

    public LoadedLibrary? Read(string packagePath)
    {
        var span = new SourceSpan(packagePath, TextLocation.Zero, TextLocation.Zero);
        if (!File.Exists(packagePath))
        {
            Error($"The library '{packagePath}' was not found.", span);
            return null;
        }

        try
        {
            using var archive = ZipFile.OpenRead(packagePath);

            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry is null)
            {
                Error($"'{packagePath}' is not an Aero package, it has no manifest.json.", span);
                return null;
            }

            using var manifestStream = manifestEntry.Open();
            var manifest = JsonNode.Parse(manifestStream) as JsonObject;
            if (manifest is null)
            {
                Error($"The manifest of '{packagePath}' is not a JSON object.", span);
                return null;
            }

            if (manifest["package_type"]?.GetValue<string>() != "#lib")
            {
                Error($"'{packagePath}' is not a library, its package type is '{manifest["package_type"]}'.", span);
                return null;
            }

            var id = manifest["package_id"]?.GetValue<string>();
            var meta = manifest["meta"] as JsonObject;
            if (string.IsNullOrWhiteSpace(id) || meta is null)
            {
                Error($"The manifest of '{packagePath}' has no package_id or meta section.", span);
                return null;
            }

            var abi = meta["abi"]?.GetValue<int>() ?? 0;
            if (abi != LibraryPackager.Abi)
            {
                Error($"The library '{id}' uses ABI {abi}, this compiler only knows ABI {LibraryPackager.Abi}.", span);
                return null;
            }

            var rid = LibraryPackager.HostRid;
            var binaryEntryName = (meta["binaries"] as JsonObject)?[rid]?.GetValue<string>();
            if (binaryEntryName is null)
            {
                Error($"The library '{id}' has no binary for '{rid}'.", span);
                return null;
            }

            var binaryEntry = archive.GetEntry(binaryEntryName);
            if (binaryEntry is null)
            {
                Error($"The library '{id}' lists the binary '{binaryEntryName}' but the package does not contain it.", span);
                return null;
            }

            var headerFolder = (meta["headers"]?.GetValue<string>() ?? "package/headers").Trim('/') + "/";
            var headers = new List<(string, string)>();
            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith(headerFolder, StringComparison.Ordinal) && e.FullName.EndsWith(".aero", StringComparison.Ordinal)).OrderBy(e => e.FullName, StringComparer.Ordinal))
            {
                using var reader = new StreamReader(entry.Open());
                headers.Add((entry.FullName[headerFolder.Length..], reader.ReadToEnd()));
            }

            if (headers.Count == 0)
            {
                Error($"The library '{id}' has no headers.", span);
                return null;
            }

            // Each library gets its own folder, two packages may ship a binary with the same name
            var folder = Path.Combine(workDirectory, $"lib{_count++}");
            Directory.CreateDirectory(folder);
            var binaryPath = Path.Combine(folder, Path.GetFileName(binaryEntryName));
            binaryEntry.ExtractToFile(binaryPath, true);

            var modules = (meta["modules"] as JsonArray)?.Select(n => n!.GetValue<string>()).ToArray() ?? [];
            var dependencies = (meta["dependencies"] as JsonArray)?.Select(d => d?["id"]?.GetValue<string>()).OfType<string>().ToArray() ?? [];
            return new LoadedLibrary(id, manifest["version"]?.GetValue<string>() ?? "0.0.0", modules, headers, binaryPath, dependencies, packagePath);
        }
        catch (Exception e) when (e is InvalidDataException or JsonException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
        {
            Error($"The library '{packagePath}' could not be read: {e.Message}", span);
            return null;
        }
    }

    // Dependencies come before the libraries that call them; a dependency that was not given is an error
    public List<LoadedLibrary>? Order(IReadOnlyList<LoadedLibrary> libraries)
    {
        var byId = new Dictionary<string, LoadedLibrary>();
        foreach (var library in libraries)
        {
            if (!byId.TryAdd(library.Id, library))
            {
                Error($"The library '{library.Id}' is given twice.", new SourceSpan(library.PackagePath, TextLocation.Zero, TextLocation.Zero));
                return null;
            }
        }

        var ordered = new List<LoadedLibrary>();
        var state = new Dictionary<string, bool>(); // false while it is being visited, true once it is placed

        bool Visit(LoadedLibrary library)
        {
            if (state.TryGetValue(library.Id, out var done))
            {
                if (done) return true;
                Error($"The library '{library.Id}' depends on itself.", new SourceSpan(library.PackagePath, TextLocation.Zero, TextLocation.Zero));
                return false;
            }

            state[library.Id] = false;
            foreach (var dependency in library.Dependencies)
            {
                if (!byId.TryGetValue(dependency, out var found))
                {
                    Error($"The library '{library.Id}' needs '{dependency}', which was not given.", new SourceSpan(library.PackagePath, TextLocation.Zero, TextLocation.Zero));
                    return false;
                }
                if (!Visit(found)) return false;
            }

            state[library.Id] = true;
            ordered.Add(library);
            return true;
        }

        foreach (var library in libraries)
            if (!Visit(library)) return null;

        return ordered;
    }
}
