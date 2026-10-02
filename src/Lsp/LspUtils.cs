using Luft.Utility;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Luft.Lsp;

public static class LspUtil
{
    // The compiler is 1-based, LSP is 0-based
    public static Position ToPosition(TextLocation l) => new(Math.Max(0, l.Line - 1), Math.Max(0, l.Column - 1));

    public static TextLocation ToLocation(Position p) => new(p.Line + 1, p.Character + 1);

    public static Range ToRange(SourceSpan s)
    {
        var a = ToPosition(s.Start);
        var b = ToPosition(s.End);
        // Zero-width or inverted spans get one character so editors still draw them
        if (b.Line < a.Line || (b.Line == a.Line && b.Character <= a.Character)) b = new Position(a.Line, a.Character + 1);
        return new Range(a, b);
    }
}