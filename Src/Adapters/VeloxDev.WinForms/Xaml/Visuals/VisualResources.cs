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
/// <para>
/// The nearest dictionary wins: the element's own <see cref="VisualElement.Resources"/>, then each ancestor's, then
/// <see cref="VisualHost.Resources"/>. A key missing from every dictionary throws instead of returning a default,
/// because a silently default value turns a typo into a blank control far from the mistake.
/// </para>
/// <para>
/// Generated code applies resource-valued attributes before the element is parented, so a static reference is
/// resolved lazily: <see cref="ApplyStatic{T}"/> retries as the ancestor chain grows, and <see cref="BindDynamic{T}"/>
/// keeps listening to every reachable dictionary so add, replace and remove all reach the target. Application code
/// normally uses <see cref="FindResource"/> or <see cref="Get{T}"/> instead.
/// </para>
/// </remarks>
public static class VisualResourceLookup
{
    /// <summary>Returns the resource stored under <paramref name="key"/>, or <see langword="null"/> when it is absent.</summary>
    /// <param name="element">The element whose resource chain is searched.</param>
    /// <param name="key">The resource key.</param>
    /// <exception cref="ArgumentNullException"><paramref name="element"/> or <paramref name="key"/> is <see langword="null"/>.</exception>
    public static object? FindResource(VisualElement element, object key)
        => TryFindResource(element, key, out var value) ? value : null;

    internal static bool TryFindResource(VisualElement element, object key, out object? value)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        if (key is null) throw new ArgumentNullException(nameof(key));

        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (current.Resources.TryGetValue(key, out value))
            {
                return true;
            }
        }

        if (element.Surface is { } surface && surface.Resources.TryGetValue(key, out value))
        {
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>Returns the resource stored under <paramref name="key"/>, converted to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The type the caller expects.</typeparam>
    /// <param name="element">The element whose resource chain is searched.</param>
    /// <param name="key">The resource key.</param>
    /// <exception cref="InvalidOperationException">The key is absent, or its value cannot become a <typeparamref name="T"/>.</exception>
    public static T Get<T>(VisualElement element, object key)
    {
        if (!TryFindResource(element, key, out var value))
        {
            throw new InvalidOperationException($"No resource named '{key}' was found in the visual element tree.");
        }

        return ConvertResource<T>(value, key);
    }

    /// <summary>
    /// Applies a static resource once it is reachable, retrying while the element's ancestor chain grows.
    /// </summary>
    /// <typeparam name="T">The type the target property expects.</typeparam>
    /// <param name="element">The element the value is applied to; also the anchor of the resource chain.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="apply">Receives the converted value.</param>
    /// <remarks>
    /// A static reference is not a live link: the subscription ends as soon as the key resolves. A key that never
    /// resolves leaves no value behind, matching a StaticResource that was not yet declared when the element was built.
    /// </remarks>
    public static void ApplyStatic<T>(VisualElement element, object key, Action<T> apply)
    {
        if (apply is null) throw new ArgumentNullException(nameof(apply));
        new ResourceBinding(
            element,
            key,
            value => apply(ConvertResource<T>(value, key)),
            dynamic: false).Start();
    }

    /// <summary>Applies a resource now and again whenever a reachable dictionary adds, replaces or removes the key.</summary>
    /// <typeparam name="T">The type the target property expects.</typeparam>
    /// <param name="element">The element the value is applied to; also the anchor of the resource chain.</param>
    /// <param name="key">The resource key.</param>
    /// <param name="apply">Receives the converted value on every update.</param>
    /// <param name="onMissing">
    /// Invoked after a previously resolved key disappears from every dictionary; clear the target's value here. A key
    /// that was never present does not invoke it, so a later declaration still applies normally.
    /// </param>
    /// <returns>A subscription released when the element is disposed, or by the caller earlier.</returns>
    public static IDisposable BindDynamic<T>(
        VisualElement element,
        object key,
        Action<T> apply,
        Action? onMissing = null)
    {
        if (apply is null) throw new ArgumentNullException(nameof(apply));
        return new ResourceBinding(
            element,
            key,
            value => apply(ConvertResource<T>(value, key)),
            dynamic: true,
            onMissing).Start();
    }

    private static T ConvertResource<T>(object? value, object key)
    {
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

// 静态引用只在解析成功后收尾；动态引用一直订阅。两者都在元素父链变化时重新挂到新的祖先字典上。
internal sealed class ResourceBinding : IDisposable
{
    private readonly VisualElement _element;
    private readonly object _key;
    private readonly Action<object?> _apply;
    private readonly bool _dynamic;
    private readonly Action? _onMissing;
    private readonly List<VisualElement> _chain = [];
    private readonly List<ResourceDictionary> _dictionaries = [];
    private VisualElement? _surfaceRoot;
    private bool _hadValue;
    private bool _disposed;

    internal ResourceBinding(
        VisualElement element,
        object key,
        Action<object?> apply,
        bool dynamic,
        Action? onMissing = null)
    {
        _element = element ?? throw new ArgumentNullException(nameof(element));
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _dynamic = dynamic;
        _onMissing = onMissing;
    }

    internal ResourceBinding Start()
    {
        _element.TrackResourceBinding(this);
        Refresh();
        return this;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unsubscribe();
    }

    private void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        RebuildChain();
        if (!VisualResourceLookup.TryFindResource(_element, _key, out var value))
        {
            // 曾经解析过、现在被移除：动态引用回到默认值；从未解析过则保持等待，稍后再声明的资源仍会生效。
            if (_dynamic && _hadValue)
            {
                _hadValue = false;
                _onMissing?.Invoke();
            }

            return;
        }

        _hadValue = true;
        _apply(value);
        if (!_dynamic)
        {
            // 静态资源一次性生效；之后字典再变化也不再改写属性。
            Dispose();
        }
    }

    // 父链可能还没接上（生成代码先赋属性后挂父级）；每变一次就重挂订阅并重试。
    private void RebuildChain()
    {
        Unsubscribe();
        VisualElement? root = null;
        for (var current = _element; current is not null; current = current.ParentElement)
        {
            _chain.Add(current);
            root = current;
            current.ParentChanged += OnChainChanged;
            var dictionary = current.Resources;
            if (_dictionaries.Contains(dictionary))
            {
                continue;
            }

            _dictionaries.Add(dictionary);
            dictionary.Changed += OnDictionaryChanged;
        }

        // 根挂到 VisualHost 之后才有表面字典；那时没有 ParentChanged，只能靠表面事件重试。
        if (root is not null)
        {
            _surfaceRoot = root;
            root.SurfaceChanged += OnChainChanged;
        }

        if (_element.Surface is { } surface && !_dictionaries.Contains(surface.Resources))
        {
            _dictionaries.Add(surface.Resources);
            surface.Resources.Changed += OnDictionaryChanged;
        }
    }

    private void Unsubscribe()
    {
        foreach (var current in _chain)
        {
            current.ParentChanged -= OnChainChanged;
        }

        _chain.Clear();

        if (_surfaceRoot is not null)
        {
            _surfaceRoot.SurfaceChanged -= OnChainChanged;
            _surfaceRoot = null;
        }

        foreach (var dictionary in _dictionaries)
        {
            dictionary.Changed -= OnDictionaryChanged;
        }

        _dictionaries.Clear();
    }

    private void OnChainChanged(object? sender, EventArgs e) => Refresh();

    private void OnDictionaryChanged(object? changedKey)
    {
        if (changedKey is null || Equals(changedKey, _key))
        {
            Refresh();
        }
    }
}
