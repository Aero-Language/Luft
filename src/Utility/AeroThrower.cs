namespace Luft.Utility;

public abstract class AeroThrower
{
    public Action<Exception>? OnError;
    protected Func<SourceSpan, string, Exception> ExceptionFactory { get; set; } = (location, message) => new Exception($"{location}: '{message}'");
    
    protected void Error(string message, SourceSpan location) => OnError?.Invoke(ExceptionFactory(location, message));
}