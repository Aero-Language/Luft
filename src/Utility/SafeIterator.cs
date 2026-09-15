namespace Luft.Utility;

public abstract class SafeIterator<TItem> : AeroThrower
{
    // Members
    protected TItem[] Items { get; set; } = Array.Empty<TItem>();
    protected int Index { get; set; }

    // Properties
    protected Func<TItem, bool> Allowed { get; init; } = _ => true;
    protected Func<TItem, bool> Denied { get; init; } = _ => false;
    protected Action<int> PopTask { get; init; } = _ => { };


    // Init
    protected void Start(TItem[] raw)
    {
        Index = 0;
        Items = raw.Where(item => Allowed(item) && !Denied(item)).ToArray();
    }
    
    
    // Iteration Methods
    protected TItem Peek(int offset = 0)
    {
        var idx = Index + offset;
        // If idx is in range, return the item
        if (Items.Length > idx) return Items[idx];
        return Items[^1]; // Otherwise return the last Item
    }
    protected TItem Pop(int amount = 1)
    {
        PopTask(amount);
        var item = Peek();
        
        Index += amount;
        return item;
    }
    
    // Error Methods
    private TItem BaseExpect(Func<TItem, bool> condition, string message, SourceSpan location, bool doPop)
    {
        var item = Peek();
        if (!condition(item))
        {
            Error(message, location);
        }

        if (doPop) Pop(); // Skip to the next Item
        return item;
    }
    protected TItem Expect(Func<TItem, bool> condition, string message, SourceSpan location) => BaseExpect(condition, message, location, true);
    protected TItem Hope(Func<TItem, bool> condition, string message, SourceSpan location) => BaseExpect(condition, message, location, false);
}