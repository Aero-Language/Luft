namespace Luft;

public record SourceSpan(string FilePath, TextLocation Start, TextLocation End)
{
    public static SourceSpan Unknown => new SourceSpan("", TextLocation.Unknown, TextLocation.Unknown);
};

public record TextLocation(int Line, int Column)
{
    public static TextLocation Unknown => new TextLocation(0, 0);
};