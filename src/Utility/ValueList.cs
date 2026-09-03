using System.Collections;

namespace Luft.Utility;

/// <summary>
/// A list that compares the items of the list instead of the list instance
/// </summary>
/// <param name="items">The enumarable list of items for the list</param>
/// <typeparam name="T">The type of items the list contains</typeparam>
public class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly T[] _items;

    public ValueList(IEnumerable<T>? items = null)
    {
        _items = items?.ToArray() ?? Array.Empty<T>();
    }

    public T this[int index] => _items[index];

    public int Count => _items.Length;

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();
    
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Equals(ValueList<T>? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return _items.SequenceEqual(other._items);
    }

    public override bool Equals(object? obj) => obj is ValueList<T> other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items)
        {
            hash.Add(item);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(ValueList<T>? left, ValueList<T>? right) => Equals(left, right);
    public static bool operator !=(ValueList<T>? left, ValueList<T>? right) => !Equals(left, right);
}