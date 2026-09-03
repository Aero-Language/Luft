namespace Luft.Utility;

public abstract class SafeCollectionIterator<TItem, TSource>
{
    public Action<Exception>? OnError;
    
    protected int ItemIndex { get; set; }

    private TItem[] RawItems { get; set; } = [];
    private Func<TItem, int, TSource> GetSource { get; set; }
    private Func<TItem, (int index, int max), bool> IsCollectionEnd { get; set; } = (_, _) => false;
    
    private Func<TItem, bool> Filter { get; set; } = _ => true; 
    protected Func<TSource, string, Exception> ExceptionFactory { get; set; } = (location, message) => new Exception($"{location}: '{message}'");
    protected TItem[] Items { get; set; } = [];
    
    
    /// <param name="getSource">Function to get the SourceSpan from TItem</param>
    /// <param name="isCollectionEnd">Indicates when a collection ends</param>
    /// <param name="filter">Whitelist filter | true = keeps the item</param>
    /// <param name="exceptionFactory">A factory that produces custom exceptions</param>
    protected void Init(Func<TItem, int, TSource> getSource, Func<TItem, (int index, int max), bool> isCollectionEnd, Func<TItem, bool>? filter = null, Func<TSource, string, Exception>? exceptionFactory = null)
    {
        GetSource = getSource;
        IsCollectionEnd = isCollectionEnd;
        if (filter != null) Filter = filter;
        if (exceptionFactory != null) ExceptionFactory = exceptionFactory;
    }

    protected void Start(TItem[] rawItems)
    {
        ItemIndex = 0;
        RawItems = rawItems;
        Items = RawItems.Where(Filter).ToArray();
    }

    protected TItem Expect(Func<TItem, bool> condition, string errorMessage, TSource? location, bool doConsume = true)
    {
        var currentToken = Peek();
        if (!condition(currentToken))
        {
            Error(errorMessage, location ?? GetSource(currentToken, ItemIndex));

            if (!IsCollectionEnd(currentToken, (ItemIndex, Items.Length)))
            {
                Consume(); 
            }
            return currentToken;
        }
        
        return doConsume ? Consume() : currentToken;
    }
    protected void Error(string message, TSource location) => OnError?.Invoke(ExceptionFactory(location, message));
    protected abstract void Synchronize();
    protected TItem Peek(int offset = 0) => ItemIndex + offset < Items.Length ? Items[ItemIndex + offset] : Items.Last();
    protected TItem Consume(int amount = 1)
    {
        var token = Peek();
        ItemIndex += amount;
        return token;
    }
}