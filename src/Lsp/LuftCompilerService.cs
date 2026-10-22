using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker;
using Luft.Utility;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

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
    private readonly Dictionary<string, FileNode> _good = new(); // last tree of each file that parsed without errors
    private readonly HashSet<string> _open = new(); // files whose text comes from the editor, not from disk

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
            var path = PathOf(uri);
            _open.Add(path);
            Parse(path, text);
            Rebuild();
        }
    }

    // A closed file goes back to what is on disk, or disappears if it was never saved
    public void CloseFile(DocumentUri uri)
    {
        lock (_gate)
        {
            var path = PathOf(uri);
            _open.Remove(path);

            try
            {
                if (File.Exists(path)) Parse(path, File.ReadAllText(path));
                else Remove(path);
            }
            catch (IOException) { }

            Rebuild();
        }
    }

    // Created, changed or deleted files reported by the client's file watcher; open files keep the editor text
    public void ApplyDiskChanges(IEnumerable<(string Path, FileChangeType Type)> changes)
    {
        lock (_gate)
        {
            foreach (var (path, type) in changes)
            {
                if (_open.Contains(path)) continue;

                if (type == FileChangeType.Deleted)
                {
                    // A deleted folder arrives as one event for the folder itself
                    var prefix = path + Path.DirectorySeparatorChar;
                    foreach (var gone in _parsed.Keys.Where(k => k == path || k.StartsWith(prefix)).ToList()) Remove(gone);
                    continue;
                }

                if (!path.EndsWith(".aero") || path.Split(Path.DirectorySeparatorChar).Any(p => SkippedDirs.Contains(p))) continue;

                try { if (File.Exists(path)) Parse(path, File.ReadAllText(path)); }
                catch (IOException) { }
            }

            Rebuild();
        }
    }

    private void Remove(string path)
    {
        _parsed.Remove(path);
        _good.Remove(path);
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
            if (!bag.HasErrors) _good[path] = ast;
        }
        catch (Exception) { }
    }

    private void Rebuild()
    {
        var bag = new DiagnosticBag();
        foreach (var file in _parsed.Values)
            foreach (var diagnostic in file.Diagnostics) bag.Add(diagnostic);

        // A file with parse errors is checked through its last good tree, so one broken file doesn't blind the rest.
        // A file that never parsed cleanly can't stand in, so then the check is skipped to avoid false errors in the others.
        var trees = new List<FileNode>();
        var broken = new HashSet<string>();
        var complete = true;

        foreach (var file in _parsed.Values)
        {
            if (!file.Diagnostics.Any(d => d.IsError)) trees.Add(file.Ast);
            else if (_good.TryGetValue(file.Path, out var good)) { trees.Add(good); broken.Add(file.Path); }
            else complete = false;
        }

        // The previous table and index stay around so hover and completion keep working while typing
        var table = Current.Table;
        var index = Current.Index;
        if (complete && trees.Count > 0)
        {
            var check = new DiagnosticBag();
            try
            {
                var fresh = new TypeLookup { Diagnostics = check }.Run(trees.ToArray(), []);
                new TypeResolver { Diagnostics = check }.Run(fresh);
                new BodyResolver { Diagnostics = check }.Run(fresh);
                table = fresh;
                index = SymbolIndex.Build(fresh);
            }
            catch (Exception) { }

            // Positions in a stale tree don't match the text, so its checker diagnostics are dropped
            foreach (var d in check)
                if (!broken.Contains(d.Span.FilePath)) bag.Add(d);
        }

        Current = new Snapshot(new Dictionary<string, ParsedFile>(_parsed), table, bag, index);
    }
}
