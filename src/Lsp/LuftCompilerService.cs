using System.Collections.Concurrent;
using Luft.Ast;
using Luft.Ast.Nodes;
using Luft.Lexer;
using Luft.TypeChecker;
using Luft.Utility;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Luft.Lsp;

public class LuftCompilerService
{
    private readonly ConcurrentDictionary<DocumentUri, string> _files = [];
    private readonly ConcurrentDictionary<DocumentUri, FileNode> _nodes = [];
    private TypeTable _table = new();

    private readonly DiagnosticBag _bag = [];
    
    private readonly Tokenizer _lexer;
    private readonly AstBuilder _parser;
    private readonly TypeLookup _lookup;
    private readonly TypeResolver _typeResolver;
    private readonly BodyResolver _bodyResolver;

    public LuftCompilerService()
    {
        _lexer = new() { Diagnostics = _bag };
        _parser = new() { Diagnostics = _bag };
        _lookup = new() { Diagnostics = _bag };
        _typeResolver = new() { Diagnostics = _bag };
        _bodyResolver = new() { Diagnostics = _bag };
    }
    
    public void UpdateFile(DocumentUri uri, string code)
    {
        _files.AddOrUpdate(uri, code, (_, _) => code);
        _bag.Clear(); // Clear the diagnostic bag before filling it again
        
        // Lex and parse the file and cache the result
        var tokens = _lexer.Tokenize(code);
        var node = _parser.BuildAst(tokens);
        _nodes.AddOrUpdate(uri, node, (_, _) => node);

        // TODO: This needs the libraries fed in
        // Regenerates the type table and runs the typeCheckers
        _table = _lookup.Run(_nodes.Values.ToArray(), []);
        _typeResolver.Run(_table);
        _bodyResolver.Run(_table);
    }

    public DocumentSymbol GetSymbolAt(DocumentUri uri, int line, int col)
    {
        return new DocumentSymbol()
        {
            
        };
    }

    public IEnumerable<AeroDiagnostic> GetDiagnostics(DocumentUri uri)
        => _bag.Where(d => Path.GetFullPath(d.Span.FilePath) == Path.GetFullPath(uri.Path)); 
}