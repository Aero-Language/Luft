namespace Luft.Utility;

public abstract class AeroThrower
{
    /// <summary>Shared sink for diagnostics. Give every stage the same bag to get one combined, ordered report.</summary>
    public DiagnosticBag? Diagnostics { get; set; }

    /// <summary>Fires for every newly reported diagnostic (of any severity) — handy for live editor updates.</summary>
    public Action<AeroDiagnostic>? OnDiagnostic;

    /// <summary>Legacy hook, kept so existing subscribers still compile. Fires for errors only, wrapping the diagnostic.</summary>
    public Action<Exception>? OnError;

    /// <summary>Which stage this class belongs to; subclasses override it so call sites don't have to repeat it.</summary>
    protected virtual CompilerStage Stage => CompilerStage.Unknown;

    protected void Error(string message, SourceSpan location, string? code = null, params RelatedInfo[] related)
        => Report(AeroSeverity.Error, message, location, code, related);

    protected void Warning(string message, SourceSpan location, string? code = null, params RelatedInfo[] related)
        => Report(AeroSeverity.Warning, message, location, code, related);

    private void Report(AeroSeverity severity, string message, SourceSpan location, string? code, RelatedInfo[] related)
    {
        var diagnostic = new AeroDiagnostic(Stage, severity, code, message, location, new ValueList<RelatedInfo>(related));

        // An identical diagnostic was already reported (same stage, code, message and span)
        if (Diagnostics is not null && !Diagnostics.Add(diagnostic)) return;

        OnDiagnostic?.Invoke(diagnostic);
        if (severity is AeroSeverity.Error) OnError?.Invoke(new AeroDiagnosticException(diagnostic));
    }
}