/*namespace Luft.Utility;

public abstract class SafeCollectionIterator<TItem> : AeroThrower
{
    protected int ItemIndex { get; set; }

    protected TItem[] RawItems { get; set; } = [];
    private Func<TItem, int, SourceSpan> GetSource { get; set; } = (_, _) => SourceSpan.Unknown;
    private Func<TItem, (int index, int max), bool> IsCollectionEnd { get; set; } = (_, _) => false;
    
    private Func<TItem, bool> Filter { get; set; } = _ => true; 
    
    protected TItem[] Items { get; set; } = [];
    
    
    /// <param name="getSource">Function to get the SourceSpan from TItem</param>
    /// <param name="isCollectionEnd">Indicates when a collection ends</param>
    /// <param name="filter">Whitelist filter | true = keeps the item</param>
    /// <param name="exceptionFactory">A factory that produces custom exceptions</param>
    protected void Init(Func<TItem, int, SourceSpan> getSource, Func<TItem, (int index, int max), bool> isCollectionEnd, Func<TItem, bool>? filter = null, Func<SourceSpan, string, Exception>? exceptionFactory = null)
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

    protected TItem Expect(Func<TItem, bool> condition, string errorMessage, SourceSpan? location, bool doConsume = true)
    {
        var currentItem = Peek();
        if (!condition(currentItem))
        {
            Error(errorMessage, location ?? GetSource(ItemIndex));

            if (!IsCollectionEnd(currentItem, (ItemIndex, Items.Length)))
            {
                Consume(); 
            }
            return currentItem;
        }
        
        return doConsume ? Consume() : currentItem;
    }
    protected TItem Peek(int offset = 0) => ItemIndex + offset < Items.Length ? Items[ItemIndex + offset] : Items.Last();
    protected TItem Consume(int amount = 1)
    {
        var token = Peek();
        ItemIndex += amount;
        return token;
    }
}*/