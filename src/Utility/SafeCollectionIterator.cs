namespace Luft.Utility;

public abstract class SafeCollectionIterator<TItem>
{
    public Action<Exception>? OnError;
    
    private int ItemIndex { get; set; } = 0;

    private List<TItem> RawItems { get; set; } = [];
    private Func<TItem, SourceSpan> GetSpan { get; set; } = _ => SourceSpan.Unknown;
    private Func<TItem, (int index, int max), bool> IsCollectionEnd { get; set; } = (_, _) => false;
    
    private Func<TItem, bool> Filter { get; set; } = _ => true; 
    private Func<SourceSpan, string, Exception> ExceptionFactory { get; init; } = (location, message) => new Exception($"{location}: '{message}'");
    private List<TItem> Items { get; set; } = [];
    
    /// <param name="getSpan">Function to get the SourceSpan from TItem</param>
    /// <param name="isCollectionEnd">Indicates when a collection ends</param>
    /// <param name="filter">Whitelist filter | true = keeps the item</param>
    protected void Init(Func<TItem, SourceSpan> getSpan, Func<TItem, (int index, int max), bool> isCollectionEnd, Func<TItem, bool>? filter = null)
    {
        GetSpan = getSpan;
        IsCollectionEnd = isCollectionEnd;
        if (filter != null) Filter = filter;
    }

    protected void Start(List<TItem> rawItems)
    {
        ItemIndex = 0;
        RawItems = rawItems;
        Items = RawItems.Where(Filter).ToList();
    }

    protected TItem Expect(Func<TItem, bool> condition, string errorMessage, SourceSpan? location = null, bool doConsume = true)
    {
        var currentToken = Peek();
        if (!condition(currentToken))
        {
            Error(errorMessage, location ?? GetSpan(currentToken));

            if (!IsCollectionEnd(currentToken, (ItemIndex, Items.Count)))
            {
                Consume(); 
            }
            return currentToken;
        }
        
        return doConsume ? Consume() : currentToken;
    }
    protected void Error(string message, SourceSpan location) => OnError?.Invoke(ExceptionFactory(location, message));
    protected abstract void Synchronize();
    protected TItem Peek(int offset = 0) => ItemIndex + offset < Items.Count ? Items[ItemIndex + offset] : Items.Last();
    protected TItem Consume()
    {
        var token = Peek();
        ItemIndex++;
        return token;
    }
}