using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker;
using Luft.Utility;
using OmniSharp.Extensions.LanguageServer.Protocol;

namespace Luft.Lsp;

public sealed record ParsedFile(string Path, string Text, Token[] Tokens, FileNode Ast, List<AeroDiagnostic> Diagnostics);

// One immutable view of the whole project; handlers read it without locking
public sealed class Snapshot(IReadOnlyDictionary<string, ParsedFile> files, TypeTable table, DiagnosticBag diagnostics, SymbolIndex index)
{
    public static readonly Snapshot Empty = new(new Dictionary<string, ParsedFile>(), new TypeTable(), new DiagnosticBag(), SymbolIndex.Empty);

    public IReadOnlyDictionary<string, ParsedFile> Files { get; } = files;
    public TypeTable Table { get; } = table;
    public DiagnosticBag Diagnostics { get; } = diagnostics;
    public SymbolIndex Index { get; } = index;
}

public class LuftCompilerService
{
    private static readonly string[] SkippedDirs = ["bin", "obj", ".git", ".idea", "node_modules"];

    private readonly object _gate = new();
    private readonly Dictionary<string, ParsedFile> _parsed = new();

    public Snapshot Current { get; private set; } = Snapshot.Empty;

    public static string PathOf(DocumentUri uri) => Normalize(uri.GetFileSystemPath());

    // Editors disagree on drive letter casing, which would otherwise load one file twice
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        return full.Length > 1 && full[1] == ':' ? char.ToUpperInvariant(full[0]) + full[1..] : full;
    }

    public void UpdateFile(DocumentUri uri, string text)
    {
        lock (_gate)
        {
            Parse(PathOf(uri), text);
            Rebuild();
        }
    }

    // A closed file goes back to what is on disk, or disappears if it was never saved
    public void CloseFile(DocumentUri uri)
    {
        lock (_gate)
        {
            var path = PathOf(uri);
            if (File.Exists(path)) Parse(path, File.ReadAllText(path));
            else _parsed.Remove(path);
            Rebuild();
        }
    }

    public void LoadWorkspace(string root)
    {
        if (!Directory.Exists(root)) return;

        lock (_gate)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.aero", SearchOption.AllDirectories))
            {
                var path = Normalize(file);
                var parts = Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar);
                if (parts.Any(p => SkippedDirs.Contains(p)) || _parsed.ContainsKey(path)) continue;

                try { Parse(path, File.ReadAllText(path)); }
                catch (IOException) { }
            }

            Rebuild();
        }
    }

    // A crash inside the lexer or parser keeps the previous result for that file instead of killing the server
    private void Parse(string path, string text)
    {
        try
        {
            var bag = new DiagnosticBag();
            var tokens = new Tokenizer { Diagnostics = bag }.TokenizeSource(text, path);
            var ast = new AstBuilder { Diagnostics = bag }.BuildAst(tokens);
            _parsed[path] = new ParsedFile(path, text, tokens, ast, bag.ToList());
        }
        catch (Exception) { }
    }

    private void Rebuild()
    {
        var bag = new DiagnosticBag();
        foreach (var file in _parsed.Values)
            foreach (var diagnostic in file.Diagnostics) bag.Add(diagnostic);

        // Same rule as the CLI: don't type-check trees that are known to be broken.
        // The previous table and index stay around so hover and completion keep working while typing.
        var table = Current.Table;
        var index = Current.Index;
        if (!bag.HasErrors && _parsed.Count > 0)
        {
            try
            {
                var fresh = new TypeLookup { Diagnostics = bag }.Run(_parsed.Values.Select(f => f.Ast).ToArray(), []);
                new TypeResolver { Diagnostics = bag }.Run(fresh);
                new BodyResolver { Diagnostics = bag }.Run(fresh);
                table = fresh;
                index = SymbolIndex.Build(fresh);
            }
            catch (Exception) { }
        }

        Current = new Snapshot(new Dictionary<string, ParsedFile>(_parsed), table, bag, index);
    }
}