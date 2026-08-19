namespace Luft.Utility;

public static class Extensions
{
    // SourceSpan extensions
    public static SourceSpan From(this SourceSpan span, TextLocation target) => span with { Start = target };
    public static SourceSpan To(this SourceSpan span, TextLocation target) => span with { End = target };
    
    // ValueList extensions
    public static ValueList<T> ToValueList<T>(this IEnumerable<T> items) => new ValueList<T>(items);
    
    
    // Helper constants
    public static HashSet<char> HexChars =>
    [
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f', 'A', 'B', 'C', 'D', 'E', 'F'
    ];
    
    public static HashSet<char> BinChars => ['0', '1'];
}