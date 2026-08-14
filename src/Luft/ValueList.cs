using System.Collections;

namespace Luft;

/// <summary>
/// A list that compares the items of the list instead of the list instance
/// </summary>
/// <param name="items">The enumarable list of items for the list</param>
/// <typeparam name="T">The type of items the list contains</typeparam>
public class ValueList<T>(IEnumerable<T> items) : IReadOnlyList<T>
{
    public ValueList() : this(Enumerable.Empty<T>()) {}
    

    public T this[int index] => items.ElementAt(index);
    
    public IEnumerator GetEnumerator() => items.GetEnumerator();
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => items.GetEnumerator();

    public int Count => items.Count();

    public bool Equals(ValueList<T> other) => items.SequenceEqual(other);
}