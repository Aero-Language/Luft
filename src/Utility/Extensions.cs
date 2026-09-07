namespace Luft.Utility;

public static class Extensions
{
    public static T OrNew<T>(this T? element) where T : new() => element ?? new T();
    public static string Repeat(this string str, int count) => string.Join(string.Empty, Enumerable.Repeat(str, count));
    
    
    // SourceSpan extensions
    public static SourceSpan From(this SourceSpan span, TextLocation target) => span with { Start = target };
    public static SourceSpan From(this SourceSpan span, SourceSpan target) => span.From(target.Start);
    public static SourceSpan To(this SourceSpan span, TextLocation target) => span with { End = target };
    public static SourceSpan To(this SourceSpan span, SourceSpan target) => span.To(target.End);
    
    // ValueList extensions
    public static ValueList<T> ToValueList<T>(this IEnumerable<T> items) => new ValueList<T>(items);
    
    // AeroType extensions
    public static AeroType ToType(this string str) => new ScalarType(str, false, false);
    
    // Identifier extensions
    public static string[] IdentifierParts(this string ident) => ident.Split(".");
    public static string FirstIdentifier(this string ident) => ident[..ident.IndexOf('.')];
    
    
    // Helper constants
    public static HashSet<char> HexChars =>
    [
        '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'a', 'b', 'c', 'd', 'e', 'f', 'A', 'B', 'C', 'D', 'E', 'F', '_'
    ];
    
    public static HashSet<char> BinChars => ['0', '1', '_'];
}