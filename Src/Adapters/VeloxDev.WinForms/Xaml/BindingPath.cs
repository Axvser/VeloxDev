using System.Collections;
using System.ComponentModel;
using System.Globalization;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// Resolves one dot-separated binding path, including simple indexers, and exposes the leaf so a two-way binding can
/// write back through the same shape it read.
/// </summary>
internal sealed class BindingPath
{
    private readonly string[] _segments;

    public BindingPath(string? path)
    {
        Raw = path ?? string.Empty;
        _segments = string.IsNullOrEmpty(Raw) || Raw == "."
            ? []
            : Raw.Split('.');
    }

    public string Raw { get; }

    public bool IsEmpty => _segments.Length == 0;

    /// <summary>The first path segment without its indexer, or <see langword="null"/> for an empty path.</summary>
    public string? FirstSegment
    {
        get
        {
            if (_segments.Length == 0)
            {
                return null;
            }

            var segment = _segments[0];
            var open = segment.IndexOf('[');
            return open >= 0 ? segment.Substring(0, open) : segment;
        }
    }

    public bool TryResolve(
        object? root,
        out object? value,
        out List<object?> nodes,
        out BindingLeaf? leaf)
    {
        value = null;
        nodes = [];
        leaf = null;

        if (IsEmpty)
        {
            value = root;
            if (root is not null)
            {
                nodes.Add(root);
            }

            return root is not null;
        }

        var current = root;
        if (current is null)
        {
            return false;
        }

        nodes.Add(current);

        for (var index = 0; index < _segments.Length; index++)
        {
            if (!TryParseSegment(_segments[index], out var name, out var indexText, out var hasIndex))
            {
                return false;
            }

            if (name.Length > 0)
            {
                var descriptor = TypeDescriptor.GetProperties(current).Find(name, ignoreCase: false);
                if (descriptor is null)
                {
                    return false;
                }

                if (!hasIndex && index == _segments.Length - 1)
                {
                    value = descriptor.GetValue(current);
                    leaf = new BindingLeaf(
                        descriptor.PropertyType,
                        descriptor.IsReadOnly ? null : () => descriptor.GetValue(current),
                        descriptor.IsReadOnly ? null : newValue => descriptor.SetValue(current, newValue));
                    return true;
                }

                current = descriptor.GetValue(current);
                if (current is null)
                {
                    return false;
                }
            }

            if (hasIndex)
            {
                nodes.Add(current);

                if (!TryGetIndexed(current, indexText, out var indexed, out var canWrite, out var setter))
                {
                    return false;
                }

                if (index == _segments.Length - 1)
                {
                    value = indexed;
                    leaf = new BindingLeaf(
                        indexed?.GetType() ?? typeof(object),
                        canWrite ? () => indexed : null,
                        canWrite ? setter : null);
                    return true;
                }

                current = indexed;
                if (current is null)
                {
                    return false;
                }
            }

            nodes.Add(current);
        }

        return false;
    }

    private static bool TryParseSegment(string segment, out string name, out string indexText, out bool hasIndex)
    {
        name = segment;
        indexText = string.Empty;
        hasIndex = false;

        var open = segment.IndexOf('[');
        if (open < 0)
        {
            return segment.Length > 0;
        }

        if (!segment.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }

        name = segment.Substring(0, open);
        indexText = segment.Substring(open + 1, segment.Length - open - 2).Trim();
        hasIndex = true;
        return name.Length > 0 || indexText.Length > 0;
    }

    private static bool TryGetIndexed(
        object? container,
        string indexText,
        out object? value,
        out bool canWrite,
        out Action<object?>? setter)
    {
        value = null;
        canWrite = false;
        setter = null;
        if (container is null)
        {
            return false;
        }

        var keyText = indexText.Trim('"', '\'');

        if (container is IList list)
        {
            if (int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
                && index >= 0
                && index < list.Count)
            {
                value = list[index];
                canWrite = !list.IsReadOnly && !list.IsFixedSize;
                setter = newValue => list[index] = newValue;
                return true;
            }

            return false;
        }

        if (container is IDictionary dictionary)
        {
            object key = int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric)
                ? numeric
                : keyText;

            if (dictionary.Contains(key))
            {
                value = dictionary[key];
                canWrite = !dictionary.IsReadOnly && !dictionary.IsFixedSize;
                setter = newValue => dictionary[key] = newValue;
                return true;
            }
        }

        return false;
    }
}

/// <summary>The writable leaf of a resolved <see cref="BindingPath"/>.</summary>
internal sealed class BindingLeaf
{
    private readonly Func<object?>? _getter;
    private readonly Action<object?>? _setter;

    public BindingLeaf(Type valueType, Func<object?>? getter, Action<object?>? setter)
    {
        ValueType = valueType;
        _getter = getter;
        _setter = setter;
    }

    public Type ValueType { get; }

    public bool CanWrite => _setter is not null;

    public object? GetValue() => _getter?.Invoke();

    public void SetValue(object? value) => _setter?.Invoke(value);
}
