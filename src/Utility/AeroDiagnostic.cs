namespace Luft.Utility;

// NOTE: the "Aero" prefix is deliberate — Microsoft.VisualStudio.LanguageServer.Protocol (used by
// the LSP project) already defines `Diagnostic` and `DiagnosticSeverity`, and these names would
// collide the moment both namespaces are imported in the same file.

public enum AeroSeverity
{
    Error,
    Warning,
    Info,
    Hint
}

/// <summary>Which compiler stage produced a diagnostic.</summary>
public enum CompilerStage
{
    Unknown,
    Lexer,
    Parser,
    TypeLookup,
    TypeResolver,
    BodyResolver
}

/// <summary>
/// A secondary location attached to a diagnostic, e.g. "first declared here" on a duplicate
/// declaration error. This is what lets an editor offer a second jump target.
/// </summary>
public sealed record RelatedInfo(SourceSpan Span, string Message);

/// <summary>
/// One problem found in the user's source. Pure data: no exception, no formatting concerns,
/// so the CLI can print it and the LSP can map it onto its own Diagnostic type later.
/// </summary>
/// <param name="Stage">The compiler stage that reported it</param>
/// <param name="Severity">Error, warning, info or hint</param>
/// <param name="Code">Optional stable id (e.g. "AERO3001"); null until a call site assigns one</param>
/// <param name="Message">Human readable description</param>
/// <param name="Span">The primary location, used for jump-to-source and squiggles</param>
/// <param name="Related">Secondary locations that help explain the problem</param>
public sealed record AeroDiagnostic(
    CompilerStage Stage,
    AeroSeverity Severity,
    string? Code,
    string Message,
    SourceSpan Span,
    ValueList<RelatedInfo> Related)
{
    public bool IsError => Severity is AeroSeverity.Error;

    // Format: path(line,col): error CODE: message — the shape most editors and terminals
    // already recognise as a clickable location.
    public override string ToString()
    {
        var where = Span.IsUnknown ? "" : $"{Span.FilePath}({Span.Start.Line},{Span.Start.Column}): ";
        var code = Code is null ? "" : $" {Code}";
        return $"{where}{Severity.ToString().ToLowerInvariant()}{code}: {Message}";
    }
}

/// <summary>Carries an <see cref="AeroDiagnostic"/> through the legacy <c>Action&lt;Exception&gt;</c> hooks.</summary>
public sealed class AeroDiagnosticException(AeroDiagnostic diagnostic) : Exception(diagnostic.ToString())
{
    public AeroDiagnostic Diagnostic { get; } = diagnostic;
}