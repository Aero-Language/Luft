using Luft.Utility;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using OmniSharp.Extensions.LanguageServer.Protocol.Server;

namespace Luft.Lsp;

public class DiagnosticPublisher(ILanguageServerFacade facade, LuftCompilerService compiler)
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _sent = new();

    // Publishes every known file, skipping the ones whose diagnostics didn't change since last time
    public void PublishAll()
    {
        var snap = compiler.Current;

        lock (_gate)
        {
            foreach (var path in snap.Files.Keys)
                Send(path, snap.Diagnostics.ForFile(path).ToList());

            foreach (var gone in _sent.Keys.Where(p => !snap.Files.ContainsKey(p)).ToList())
            {
                Send(gone, new List<AeroDiagnostic>());
                _sent.Remove(gone);
            }
        }
    }

    private void Send(string path, List<AeroDiagnostic> diagnostics)
    {
        var signature = string.Join("\n", diagnostics.Select(d => $"{d.Severity}|{d.Span}|{d.Message}"));
        if (_sent.TryGetValue(path, out var previous) && previous == signature) return;
        _sent[path] = signature;

        var mapped = diagnostics.Select(d => new Diagnostic
        {
            Message = d.Message,
            Severity = d.Severity switch
            {
                AeroSeverity.Warning => DiagnosticSeverity.Warning,
                AeroSeverity.Info => DiagnosticSeverity.Information,
                AeroSeverity.Hint => DiagnosticSeverity.Hint,
                _ => DiagnosticSeverity.Error
            },
            Range = LspUtil.ToRange(d.Span),
            Source = "luft"
        }).ToList();

        facade.TextDocument.PublishDiagnostics(new PublishDiagnosticsParams
        {
            Uri = DocumentUri.FromFileSystemPath(path),
            Diagnostics = new Container<Diagnostic>(mapped)
        });
    }
}