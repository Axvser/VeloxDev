using System.ComponentModel;

namespace VeloxDev.WinForms.Xaml;

/// <summary>
/// The live link behind one <see cref="Binding"/> on a dependency property.
/// </summary>
/// <remarks>
/// The source is resolved lazily: generated code often calls <c>SetBinding</c> while the control is still being
/// constructed, before anyone has assigned <see cref="VeloxDev.WinForms.Xaml.Controls.Control.DataContext"/>. Attaching later is what keeps
/// bindings out of the initialization order.
/// </remarks>
internal sealed class BindingExpression : IDisposable
{
    private readonly VeloxDev.WinForms.Xaml.Controls.Control _owner;
    private readonly System.Windows.Forms.Control _target;
    private readonly DependencyProperty _property;
    private readonly Binding _definition;
    private readonly List<INotifyPropertyChanged> _observed = [];

    private object? _source;
    private bool _attached;
    private bool _applying;
    private bool _disposed;

    public BindingExpression(VeloxDev.WinForms.Xaml.Controls.Control owner, DependencyProperty property, Binding definition)
        : this(owner, owner, property, definition)
    {
    }

    /// <summary>Creates an expression whose value target is any control, not only the data-context host.</summary>
    public BindingExpression(VeloxDev.WinForms.Xaml.Controls.Control owner, System.Windows.Forms.Control target, DependencyProperty property, Binding definition)
    {
        _owner = owner;
        _target = target;
        _property = property;
        _definition = definition;
    }

    /// <summary>The control receiving the bound value.</summary>
    public System.Windows.Forms.Control Target => _target;

    /// <summary>The dependency property receiving the bound value.</summary>
    public DependencyProperty Property => _property;

    /// <summary>Attaches to the current effective data context, or stays idle while there is none.</summary>
    public void Attach()
    {
        if (_disposed)
        {
            return;
        }

        var source = _owner.DataContext;
        if (source is null)
        {
            DetachSource();
            return;
        }

        if (_attached && ReferenceEquals(source, _source))
        {
            return;
        }

        DetachSource();
        _source = source;
        _attached = true;
        ApplyToTarget();
    }

    /// <summary>Re-reads the data context and rebuilds the subscription.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        DetachSource();
        Attach();
    }

    /// <summary>Called after the target dependency property changed; writes back for a two-way binding.</summary>
    public void OnTargetValueChanged(object? oldValue, object? newValue, XamlValueSource source)
    {
        if (_disposed || _definition.Mode != BindingMode.TwoWay || source != XamlValueSource.Local || _applying)
        {
            return;
        }

        if (_source is null || !TryResolve(out var current, out _, out var leafOwner, out var leaf) || leaf is null)
        {
            return;
        }

        var converted = _definition.Converter?.ConvertBack(newValue, leaf.PropertyType)
            ?? XamlRuntime.ConvertValue(newValue, leaf.PropertyType);

        if (Equals(current, converted))
        {
            return;
        }

        _applying = true;
        try
        {
            leaf.SetValue(leafOwner, converted);
        }
        finally
        {
            _applying = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _disposed = true;
        DetachSource();
    }

    private void ApplyToTarget()
    {
        if (_disposed || _source is null || _target.IsDisposed || _target.Disposing)
        {
            Observe(nodes: null);
            return;
        }

        if (!TryResolve(out var value, out var nodes, out _, out _))
        {
            Observe(nodes: null);
            return;
        }

        Observe(nodes);

        var converted = _definition.Converter?.Convert(value, _property.PropertyType)
            ?? XamlRuntime.ConvertValue(value, _property.PropertyType);

        if (!Equals(_property.GetValue(_target), converted))
        {
            _applying = true;
            try
            {
                // A Control target goes through the change pipeline so triggers and the derived control see
                // the bound value exactly as they see a local write. A native control has no such pipeline.
                if (_target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
                {
                    _property.SetValueCore(xaml, converted, XamlValueSource.Binding);
                }
                else
                {
                    _property.SetValue(_target, converted);
                }
            }
            finally
            {
                _applying = false;
            }
        }

        if (_definition.Mode == BindingMode.OneTime)
        {
            // OneTime 每个数据上下文只推一次；之后换 DataContext 会自然重挂。
            DetachSource();
        }
    }

    private bool TryResolve(
        out object? value,
        out List<object?> nodes,
        out object? leafOwner,
        out PropertyDescriptor? leaf)
    {
        nodes = [];
        value = null;
        leafOwner = null;
        leaf = null;

        var current = _source;
        if (current is null)
        {
            return false;
        }

        nodes.Add(current);

        var path = _definition.Path;
        if (string.IsNullOrEmpty(path) || path == ".")
        {
            value = current;
            return true;
        }

        var segments = path.Split('.');
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (segment.Length == 0 || segment.IndexOf('[') >= 0)
            {
                return false;
            }

            var descriptor = TypeDescriptor.GetProperties(current).Find(segment, ignoreCase: false);
            if (descriptor is null)
            {
                return false;
            }

            if (index == segments.Length - 1)
            {
                leafOwner = current;
                leaf = descriptor;
                value = descriptor.GetValue(current);
                return true;
            }

            current = descriptor.GetValue(current);
            if (current is null)
            {
                return false;
            }

            nodes.Add(current);
        }

        return false;
    }

    private void Observe(List<object?>? nodes)
    {
        var next = new List<INotifyPropertyChanged>();
        if (nodes is not null)
        {
            foreach (var node in nodes)
            {
                if (node is INotifyPropertyChanged observable && !next.Exists(existing => ReferenceEquals(existing, observable)))
                {
                    next.Add(observable);
                }
            }
        }

        foreach (var previous in _observed)
        {
            if (!next.Exists(candidate => ReferenceEquals(candidate, previous)))
            {
                previous.PropertyChanged -= OnSourcePropertyChanged;
            }
        }

        foreach (var candidate in next)
        {
            if (!_observed.Exists(previous => ReferenceEquals(previous, candidate)))
            {
                candidate.PropertyChanged += OnSourcePropertyChanged;
            }
        }

        _observed.Clear();
        _observed.AddRange(next);
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // 路径的中间对象可能已经换了；ApplyToTarget 会重新订阅。
        ApplyToTarget();
    }

    private void DetachSource()
    {
        foreach (var observable in _observed)
        {
            observable.PropertyChanged -= OnSourcePropertyChanged;
        }

        _observed.Clear();
        _source = null;
        _attached = false;
    }
}
