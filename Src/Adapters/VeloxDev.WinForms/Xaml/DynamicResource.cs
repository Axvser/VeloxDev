using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml;

/// <summary>A keyed reference to a resource that is re-resolved when the owning dictionaries change.</summary>
/// <remarks>
/// The runtime helper is <see cref="XamlRuntime.SetDynamicResource"/>; this type exists so callers and generated code
/// can pass a key together with the intent of a live lookup instead of a one-time value.
/// </remarks>
public sealed class DynamicResource
{
    /// <summary>Creates a reference to <paramref name="key"/>.</summary>
    /// <param name="key">The resource key.</param>
    public DynamicResource(object key)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
    }

    /// <summary>The resource key.</summary>
    public object Key { get; }
}

/// <summary>
/// Walks a <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/> ancestor chain, resolves one resource key, and raises a callback
/// whenever a relevant dictionary changes or the control moves in the tree.
/// </summary>
internal sealed class ResourceChainObserver : IDisposable
{
    private readonly WeakReference<VeloxDev.WinForms.Xaml.Controls.Control> _owner;
    private readonly object _key;
    private readonly Action _changed;
    private readonly List<ResourceDictionary> _subscribed = [];
    private readonly List<System.Windows.Forms.Control> _watched = [];

    private bool _hookedOwner;
    private bool _disposed;

    public ResourceChainObserver(
        VeloxDev.WinForms.Xaml.Controls.Control owner,
        object key,
        Action changed)
    {
        _owner = new WeakReference<VeloxDev.WinForms.Xaml.Controls.Control>(owner);
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));

        owner.Disposed += OnOwnerDisposed;
        _hookedOwner = true;
        RefreshSubscriptions();
    }

    /// <summary>The value last resolved, or <see langword="null"/> when the key is absent.</summary>
    public object? Value { get; private set; }

    /// <summary>Whether the last refresh found the key.</summary>
    public bool Found { get; private set; }

    /// <summary>Re-walks the tree and rebuilds the dictionary subscriptions.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        RefreshSubscriptions();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DetachDictionaries();
        DetachWatchedControls();

        if (_hookedOwner && _owner.TryGetTarget(out var owner))
        {
            owner.Disposed -= OnOwnerDisposed;
        }

        _hookedOwner = false;
        Value = null;
        Found = false;
    }

    private void RefreshSubscriptions()
    {
        DetachDictionaries();
        DetachWatchedControls();
        Value = null;
        Found = false;

        if (!_owner.TryGetTarget(out var owner) || owner.IsDisposed || owner.Disposing)
        {
            Dispose();
            return;
        }

        // 整条祖先链都盯住 ParentChanged：子树先建好、再整体挂到某个祖先下时，只有链上某个控件
        // 会收到通知；只盯 owner 的话，祖先后来又挂到别处就再也看不到新字典。
        for (System.Windows.Forms.Control? current = owner; current is not null; current = current.Parent)
        {
            current.ParentChanged += OnOwnerParentChanged;
            _watched.Add(current);

            if (current is not VeloxDev.WinForms.Xaml.Controls.Control xaml)
            {
                continue;
            }

            xaml.Resources.Changed += OnDictionaryChanged;
            _subscribed.Add(xaml.Resources);

            if (!Found && xaml.Resources.TryFindSource(_key, out var value, out _))
            {
                // 局部字典先于祖先；第一个命中的就是最终值。
                Value = value;
                Found = true;
            }
        }
    }

    private void DetachWatchedControls()
    {
        foreach (var control in _watched)
        {
            control.ParentChanged -= OnOwnerParentChanged;
        }

        _watched.Clear();
    }

    private void DetachDictionaries()
    {
        foreach (var dictionary in _subscribed)
        {
            dictionary.Changed -= OnDictionaryChanged;
        }

        _subscribed.Clear();
    }

    private void OnDictionaryChanged(object? changedKey)
    {
        if (_disposed)
        {
            return;
        }

        if (changedKey is not null && !Equals(changedKey, _key))
        {
            return;
        }

        RefreshSubscriptions();
        if (!_disposed)
        {
            _changed();
        }
    }

    private void OnOwnerParentChanged(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        RefreshSubscriptions();
        if (!_disposed)
        {
            _changed();
        }
    }

    private void OnOwnerDisposed(object? sender, EventArgs e) => Dispose();
}

/// <summary>
/// A <see cref="Binding"/> source that exposes the current value of one resource key.
/// </summary>
/// <remarks>
/// A style or trigger setter cannot point at a control's dependency property before the setter is applied. Expressing
/// the lookup as a binding over this object lets the existing setter-binding path follow dictionary replacement
/// without a new application pipeline.
/// </remarks>
internal sealed class DynamicResourceProvider : INotifyPropertyChanged, IDisposable
{
    private readonly ResourceChainObserver _observer;
    private object? _value;
    private bool _disposed;

    public DynamicResourceProvider(VeloxDev.WinForms.Xaml.Controls.Control owner, object key)
    {
        _observer = new ResourceChainObserver(owner, key, OnResourceChanged);
        _value = _observer.Found ? _observer.Value : null;
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>The resource value currently found for the key, or <see langword="null"/>.</summary>
    public object? Value => _value;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _observer.Dispose();
        _value = null;
    }

    private void OnResourceChanged()
    {
        if (_disposed)
        {
            return;
        }

        var next = _observer.Found ? _observer.Value : null;
        if (Equals(next, _value))
        {
            return;
        }

        _value = next;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
    }
}

/// <summary>Keeps one dependency property of one target following one resource key.</summary>
internal sealed class DynamicResourceState : IDisposable
{
    private readonly WeakReference<VeloxDev.WinForms.Xaml.Controls.Control> _owner;
    private readonly WeakReference<System.Windows.Forms.Control> _target;
    private readonly DependencyProperty _property;
    private readonly object _key;
    private readonly ResourceChainObserver _observer;

    private object? _appliedValue;
    private bool _applied;
    private bool _disposed;

    public DynamicResourceState(
        VeloxDev.WinForms.Xaml.Controls.Control owner,
        System.Windows.Forms.Control target,
        DependencyProperty property,
        object key)
    {
        _owner = new WeakReference<VeloxDev.WinForms.Xaml.Controls.Control>(owner);
        _target = new WeakReference<System.Windows.Forms.Control>(target);
        _property = property;
        _key = key;

        target.Disposed += OnTargetDisposed;
        _observer = new ResourceChainObserver(owner, key, OnResourceChanged);
        Apply();
    }

    /// <summary>The key this state follows.</summary>
    public object Key => _key;

    /// <summary>Whether the state still describes the same owner, target, property and key.</summary>
    public bool Matches(
        VeloxDev.WinForms.Xaml.Controls.Control owner,
        System.Windows.Forms.Control target,
        DependencyProperty property,
        object key)
    {
        return ReferenceEquals(_property, property)
            && Equals(_key, key)
            && _owner.TryGetTarget(out var existingOwner)
            && ReferenceEquals(existingOwner, owner)
            && _target.TryGetTarget(out var existingTarget)
            && ReferenceEquals(existingTarget, target);
    }

    /// <summary>Re-resolves the resource and applies or clears the value.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        _observer.Refresh();
        Apply();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _observer.Dispose();
        if (_target.TryGetTarget(out var target))
        {
            target.Disposed -= OnTargetDisposed;
        }
    }

    private void OnResourceChanged()
    {
        if (_disposed)
        {
            return;
        }

        Apply();
    }

    private void Apply()
    {
        if (!_target.TryGetTarget(out var target) || target.IsDisposed || target.Disposing)
        {
            Dispose();
            return;
        }

        if (!_observer.Found || _observer.Value is null)
        {
            if (_applied)
            {
                // 资源被移除时回到默认/继承值；从未应用过则保留作者原来的本地值。
                _property.ClearValue(target);
                _applied = false;
                _appliedValue = null;
            }

            return;
        }

        if (_applied && Equals(_appliedValue, _observer.Value))
        {
            return;
        }

        _property.SetValue(target, _observer.Value);
        _appliedValue = _observer.Value;
        _applied = true;
    }

    private void OnTargetDisposed(object? sender, EventArgs e) => Dispose();
}
