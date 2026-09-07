namespace Luft.Utility;

public record SourceSpan(string FilePath, TextLocation Start, TextLocation End)
{
    public static SourceSpan Unknown => new SourceSpan("", TextLocation.Zero, TextLocation.Zero);

    public override string ToString() => $"{FilePath}[{Start}-{End}]";
}

public record TextLocation(int Line, int Column)
{
    public static TextLocation Zero => new TextLocation(0, 0);
    
    public override string ToString() => $"{Line}:{Column}";
}