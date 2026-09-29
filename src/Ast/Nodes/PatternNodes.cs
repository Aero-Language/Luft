using Luft.Utility;

namespace Luft.Ast.Nodes;

public abstract record PatternNode(SourceSpan Span) : AstNode(Span);

// A literal or named constant the target is compared to, e.g. `0`, `GameState.Menu`, `null`.
public record ConstantPattern(ExpressionNode Value, SourceSpan Span) : PatternNode(Span);

// `is Type` or `is Type binding`. The binding is only honored inside a match case — a plain
// `is` test used as a boolean expression never introduces one.
public record TypePattern(AeroType Type, string? Binding, SourceSpan Span) : PatternNode(Span);

// `in a..b`
public record RangePattern(RangeExpressionNode Range, SourceSpan Span) : PatternNode(Span);

public record OrPattern(PatternNode Left, PatternNode Right, SourceSpan Span) : PatternNode(Span);

// The `else` case in a match — matches whatever's left, must come last.
public record ElsePattern(SourceSpan Span) : PatternNode(Span);

// `x is Type`, `x is not Type`, `x in a..b` — usable anywhere a Bool expression is, not just in match.
public record PatternTestExpressionNode
(
    ExpressionNode Target,
    PatternNode Pattern,
    bool Negated,
    SourceSpan Span
) : ExpressionNode(Span);