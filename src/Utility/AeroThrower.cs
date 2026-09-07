namespace Luft.Utility;

public abstract class AeroThrower<TSource>
{
    public Action<Exception>? OnError;
    protected Func<TSource, string, Exception> ExceptionFactory { get; set; } = (location, message) => new Exception($"{location}: '{message}'");
    
    protected void Error(string message, TSource location) => OnError?.Invoke(ExceptionFactory(location, message));
}