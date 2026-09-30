using System.Collections;

namespace Luft.Utility;

/// <summary>
/// Collects diagnostics from every compiler stage so nothing has to throw to report an error.
/// Identical diagnostics (same stage, code, message and span) are stored only once, which also
/// cuts down repeated reports of the same problem.
/// </summary>
public sealed class DiagnosticBag : IReadOnlyList<AeroDiagnostic>
{
    private readonly List<AeroDiagnostic> _items = [];
    private readonly HashSet<AeroDiagnostic> _seen = [];

    public int Count => _items.Count;
    public AeroDiagnostic this[int index] => _items[index];

    public int ErrorCount => _items.Count(d => d.IsError);
    public bool HasErrors => _items.Any(d => d.IsError);

    /// <returns>false if an identical diagnostic was already present</returns>
    public bool Add(AeroDiagnostic diagnostic)
    {
        if (!_seen.Add(diagnostic)) return false;
        _items.Add(diagnostic);
        return true;
    }

    public void Clear()
    {
        _items.Clear();
        _seen.Clear();
    }

    /// <summary>Everything, sorted by file, then line, then column.</summary>
    public IEnumerable<AeroDiagnostic> Ordered() => _items
        .OrderBy(d => d.Span.FilePath, StringComparer.Ordinal)
        .ThenBy(d => d.Span.Start.Line)
        .ThenBy(d => d.Span.Start.Column);

    public IEnumerable<AeroDiagnostic> ForFile(string filePath) => Ordered().Where(d => d.Span.FilePath == filePath);

    /// <summary>The diagnostics covering a cursor position — the building block for hover and quick-fix lookups.</summary>
    public IEnumerable<AeroDiagnostic> At(string filePath, TextLocation location)
        => _items.Where(d => d.Span.FilePath == filePath && d.Span.Contains(location));

    public IEnumerator<AeroDiagnostic> GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}