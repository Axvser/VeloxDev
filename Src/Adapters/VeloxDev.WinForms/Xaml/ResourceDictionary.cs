using System.Collections;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// The resource store behind <see cref="VeloxDev.WinForms.Xaml.Controls.Control.Resources"/>. It is a normal dictionary plus a change signal so
/// implicit styles can be re-evaluated when the resource with their type key is added, replaced or removed.
/// </summary>
public sealed class ResourceDictionary : IDictionary<object, object?>
{
    private readonly Dictionary<object, object?> _items = [];

    /// <summary>Raised after a key is added, replaced or removed; <see langword="null"/> means the dictionary was cleared.</summary>
    public event Action<object?>? Changed;

    /// <inheritdoc />
    public object? this[object key]
    {
        get => _items[key];
        set
        {
            _items[key] = value;
            Changed?.Invoke(key);
        }
    }

    /// <inheritdoc />
    public ICollection<object> Keys => _items.Keys;

    /// <inheritdoc />
    public ICollection<object?> Values => _items.Values;

    /// <inheritdoc />
    public int Count => _items.Count;

    /// <inheritdoc />
    public bool IsReadOnly => false;

    /// <inheritdoc />
    public void Add(object key, object? value)
    {
        _items.Add(key, value);
        Changed?.Invoke(key);
    }

    /// <inheritdoc />
    public bool ContainsKey(object key) => _items.ContainsKey(key);

    /// <inheritdoc />
    public bool Remove(object key)
    {
        var removed = _items.Remove(key);
        if (removed)
        {
            Changed?.Invoke(key);
        }

        return removed;
    }

    /// <inheritdoc />
    public bool TryGetValue(object key, out object? value) => _items.TryGetValue(key, out value);

    /// <inheritdoc />
    public void Clear()
    {
        _items.Clear();
        Changed?.Invoke(null);
    }

    void ICollection<KeyValuePair<object, object?>>.Add(KeyValuePair<object, object?> item)
        => Add(item.Key, item.Value);

    bool ICollection<KeyValuePair<object, object?>>.Contains(KeyValuePair<object, object?> item)
        => ((ICollection<KeyValuePair<object, object?>>)_items).Contains(item);

    void ICollection<KeyValuePair<object, object?>>.CopyTo(KeyValuePair<object, object?>[] array, int arrayIndex)
        => ((ICollection<KeyValuePair<object, object?>>)_items).CopyTo(array, arrayIndex);

    bool ICollection<KeyValuePair<object, object?>>.Remove(KeyValuePair<object, object?> item)
    {
        var removed = ((ICollection<KeyValuePair<object, object?>>)_items).Remove(item);
        if (removed)
        {
            Changed?.Invoke(item.Key);
        }

        return removed;
    }

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<object, object?>> GetEnumerator() => _items.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
