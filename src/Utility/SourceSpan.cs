namespace Luft.Utility;

/// <summary>
/// A range inside one file. Lines and columns are 1-based (as produced by the Tokenizer),
/// and <see cref="End"/> is exclusive: it points at the first character AFTER the span.
/// </summary>
public record SourceSpan(string FilePath, TextLocation Start, TextLocation End)
{
    public static SourceSpan Unknown => new SourceSpan("", TextLocation.Zero, TextLocation.Zero);

    public bool IsUnknown => FilePath.Length == 0;

    /// <summary>True if <paramref name="location"/> lies inside this span (start inclusive, end exclusive).</summary>
    public bool Contains(TextLocation location) => Start <= location && location < End;

    public override string ToString() => $"{FilePath}[{Start}-{End}]";
}

public record TextLocation(int Line, int Column) : IComparable<TextLocation>
{
    public static TextLocation Zero => new TextLocation(0, 0);

    public int CompareTo(TextLocation? other)
    {
        if (other is null) return 1;
        var byLine = Line.CompareTo(other.Line);
        return byLine != 0 ? byLine : Column.CompareTo(other.Column);
    }

    public static bool operator <(TextLocation a, TextLocation b) => a.CompareTo(b) < 0;
    public static bool operator >(TextLocation a, TextLocation b) => a.CompareTo(b) > 0;
    public static bool operator <=(TextLocation a, TextLocation b) => a.CompareTo(b) <= 0;
    public static bool operator >=(TextLocation a, TextLocation b) => a.CompareTo(b) >= 0;

    public override string ToString() => $"{Line}:{Column}";
}