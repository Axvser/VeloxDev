namespace VeloxDev.WinForms.Xaml.Visuals;

/// <summary>Holds the named parts of one created <see cref="VisualControlTemplate"/> instance.</summary>
/// <remarks>
/// A scope belongs to one template instance, not to the template description. A template applied to many elements
/// therefore gets one scope per element, and a part name never resolves to another element's part.
/// </remarks>
public sealed class VisualNameScope
{
    private readonly Dictionary<string, object> _names = new(StringComparer.Ordinal);

    /// <summary>Registers <paramref name="value"/> under <paramref name="name"/>.</summary>
    /// <param name="name">The part name declared with <c>x:Name</c>.</param>
    /// <param name="value">The element or object the name refers to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="value"/> is <see langword="null"/>.</exception>
    public void Register(string name, object value)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        if (value is null) throw new ArgumentNullException(nameof(value));
        _names[name] = value;
    }

    /// <summary>Returns the object registered under <paramref name="name"/>, or <see langword="null"/>.</summary>
    /// <param name="name">The part name to look up.</param>
    public object? Find(string name)
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        return _names.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>Returns the object registered under <paramref name="name"/> when it is a <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type the caller expects.</typeparam>
    /// <param name="name">The part name to look up.</param>
    public T? Find<T>(string name) where T : class
    {
        if (name is null) throw new ArgumentNullException(nameof(name));
        return _names.TryGetValue(name, out var value) ? value as T : null;
    }
}

/// <summary>Resolves named resources for the visual backend through the element's ancestor chain and its surface.</summary>
/// <remarks>
/// The nearest dictionary wins: the element's own <see cref="VisualElement.Resources"/>, then each ancestor's, then
/// <see cref="VisualSurface.Resources"/>. A key missing from every dictionary throws instead of returning a default,
/// because a silently default value turns a typo into a blank control far from the mistake.
/// </remarks>
public static class VisualResourceLookup
{
    /// <summary>Returns the resource stored under <paramref name="key"/>, or <see langword="null"/> when it is absent.</summary>
    /// <param name="element">The element whose resource chain is searched.</param>
    /// <param name="key">The resource key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    public static object? FindResource(VisualElement element, object key)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        if (key is null) throw new ArgumentNullException(nameof(key));

        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (current.Resources.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return element.Surface is { } surface && surface.Resources.TryGetValue(key, out var surfaceValue)
            ? surfaceValue
            : null;
    }

    /// <summary>Returns the resource stored under <paramref name="key"/>, converted to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type the caller expects.</typeparam>
    /// <param name="element">The element whose resource chain is searched.</param>
    /// <param name="key">The resource key.</param>
    /// <exception cref="InvalidOperationException">The key is absent, or its value cannot become a <typeparamref name="T"/>.</exception>
    public static T Get<T>(VisualElement element, object key)
    {
        var value = FindResource(element, key)
            ?? throw new InvalidOperationException($"No resource named '{key}' was found in the visual element tree.");
        if (value is T typed)
        {
            return typed;
        }

        var converted = XamlRuntime.ConvertValue(value, typeof(T));
        return converted is T result
            ? result
            : throw new InvalidOperationException($"Resource '{key}' cannot be converted to '{typeof(T)}'.");
    }
}
