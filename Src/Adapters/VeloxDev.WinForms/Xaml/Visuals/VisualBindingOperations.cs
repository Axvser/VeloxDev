using System.ComponentModel;
using System.Globalization;
using VeloxDev.WinForms.Xaml.Visuals;

namespace VeloxDev.WinForms.Xaml;

/// <summary>Path bindings for visual-backend objects; the source syntax is the existing WPF-style <see cref="Binding"/>.</summary>
/// <remarks>
/// <para>
/// Visual elements implement <see cref="IWinformsObject"/>, so this expression resolves the same
/// <see cref="Binding.Path"/> against the effective <see cref="VisualElement.DataContext"/> and writes the target
/// through the priority store.
/// </para>
/// <para>
/// Supported: <see cref="BindingMode.OneWay"/>/<see cref="BindingMode.OneTime"/>/<see cref="BindingMode.TwoWay"/>,
/// simple dot paths with indexers, <see cref="Binding.Source"/>, converters, <see cref="Binding.StringFormat"/>,
/// <see cref="Binding.FallbackValue"/> and <see cref="Binding.TargetNullValue"/>. <see cref="Binding.ElementName"/>
/// resolves a named element in the target's visual tree; <see cref="Binding.RelativeSource"/> supports
/// <c>Self</c> and <c>TemplatedParent</c>.
/// </para>
/// </remarks>
public static class VisualBindingOperations
{
    /// <summary>Binds <paramref name="property"/> on <paramref name="target"/> to <paramref name="binding"/>.</summary>
    /// <param name="target">The visual-backend object receiving values.</param>
    /// <param name="property">The property to write.</param>
    /// <param name="binding">The WPF-style binding description.</param>
    /// <returns>A subscription that detaches the binding when disposed.</returns>
    public static IDisposable Bind(IWinformsObject target, DependencyProperty property, Binding binding)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (binding is null) throw new ArgumentNullException(nameof(binding));

        var expression = new VisualBindingExpression(target, property, binding);
        expression.Attach();
        return expression;
    }
}

/// <summary>Extension helpers for visual path bindings.</summary>
public static class VisualBindingExtensions
{
    /// <summary>Binds <paramref name="property"/> to a WPF-style <paramref name="binding"/>.</summary>
    /// <param name="target">The visual-backend object receiving values.</param>
    /// <param name="property">The property to write.</param>
    /// <param name="binding">The binding description.</param>
    /// <returns>A subscription that detaches the binding when disposed.</returns>
    public static IDisposable SetBinding(this IWinformsObject target, DependencyProperty property, Binding binding)
        => VisualBindingOperations.Bind(target, property, binding);
}

internal sealed class VisualBindingExpression : IDisposable
{
    private readonly IWinformsObject _target;
    private readonly DependencyProperty _property;
    private readonly Binding _binding;
    private readonly BindingPath _path;
    private readonly List<INotifyPropertyChanged> _nodes = [];
    private INotifyPropertyChanged? _source;
    private VisualElement? _dataContextParent;
    private BindingLeaf? _leaf;
    private bool _updatingTarget;
    private bool _updatingSource;
    private bool _disposed;

    public VisualBindingExpression(IWinformsObject target, DependencyProperty property, Binding binding)
    {
        _target = target;
        _property = property;
        _binding = binding;
        _path = new BindingPath(binding.Path);
    }

    public void Attach()
    {
        if (_target is WinformsObject visualTarget)
        {
            visualTarget.PropertyChanged += OnTargetPropertyChanged;
        }

        AttachDataContextParent();

        // 绑定可能在挂上父级之前就建立（预构造子树、复用视图）；任何绑定都要在父级变化后重解源，
        // 否则 ElementName 与继承来的 DataContext 会一直停在建立时看到的空源上。
        if (_target is VisualElement parentedTarget)
        {
            parentedTarget.ParentChanged += OnTargetParentChanged;
        }

        if (_binding.Mode != BindingMode.OneTime)
        {
            AttachSource();
        }

        UpdateTarget();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_target is WinformsObject visualTarget)
        {
            visualTarget.PropertyChanged -= OnTargetPropertyChanged;
        }

        if (_target is VisualElement contextTarget)
        {
            contextTarget.ParentChanged -= OnTargetParentChanged;
        }

        DetachDataContextParent();
        DetachSource();
    }

    // DataContext 绑定以父元素的 DataContext 为源；父上下文换了以后必须重求值，否则复用视图会停在旧槽位。
    private void AttachDataContextParent()
    {
        if (!ReferenceEquals(_property, VisualElement.DataContextProperty) || _target is not VisualElement target)
        {
            return;
        }

        var parent = target.ParentElement;
        if (ReferenceEquals(parent, _dataContextParent))
        {
            return;
        }

        DetachDataContextParent();
        _dataContextParent = parent;
        if (parent is not null)
        {
            parent.PropertyChanged += OnDataContextParentChanged;
        }
    }

    private void DetachDataContextParent()
    {
        if (_dataContextParent is { } parent)
        {
            parent.PropertyChanged -= OnDataContextParentChanged;
            _dataContextParent = null;
        }
    }

    private void OnTargetParentChanged(object? sender, EventArgs e)
    {
        AttachDataContextParent();
        AttachSource();
        UpdateTarget();
    }

    private void OnDataContextParentChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (ReferenceEquals(e.Property, VisualElement.DataContextProperty))
        {
            AttachSource();
            UpdateTarget();
        }
    }

    private void AttachSource()
    {
        DetachSource();
        var source = ResolveSource();
        if (source is null)
        {
            _source = null;
            return;
        }

        _source = source as INotifyPropertyChanged;
    }

    private void DetachSource()
    {
        _source = null;

        foreach (var node in _nodes)
        {
            node.PropertyChanged -= OnSourcePropertyChanged;
        }

        _nodes.Clear();
        _leaf = null;
    }

    private object? ResolveSource()
    {
        if (_binding.Source is not null)
        {
            return _binding.Source;
        }

        if (!string.IsNullOrWhiteSpace(_binding.ElementName) && _target is VisualElement namedTarget)
        {
            if (FindNamedElement(namedTarget, _binding.ElementName!) is { } named)
            {
                return named;
            }

            return null;
        }

        if (_binding.RelativeSource is { Mode: RelativeSourceMode.Self })
        {
            return _target;
        }

        if (_binding.RelativeSource is { Mode: RelativeSourceMode.TemplatedParent })
        {
            return (_target as VisualElement)?.TemplatedParent;
        }

        // A binding whose target IS DataContext resolves against the inherited parent context. Reading the
        // target's own DataContext would re-evaluate the path against the value it just produced.
        var element = _target as VisualElement;
        return ReferenceEquals(_property, VisualElement.DataContextProperty)
            ? element?.ParentElement?.DataContext
            : element?.DataContext;
    }

    private static VisualElement? FindNamedElement(VisualElement target, string name)
    {
        var root = target;
        while (root.ParentElement is { } parent)
        {
            root = parent;
        }

        return Find(root);

        VisualElement? Find(VisualElement element)
        {
            if (string.Equals(element.Name, name, StringComparison.Ordinal))
            {
                return element;
            }

            foreach (var child in element.Children)
            {
                if (child is VisualElement nested && Find(nested) is { } found)
                {
                    return found;
                }
            }

            return null;
        }
    }

    private void OnSourcePropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateTarget();

    private void OnTargetPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (ReferenceEquals(e.Property, VisualElement.DataContextProperty))
        {
            if (_binding.Mode == BindingMode.OneTime)
            {
                return;
            }

            AttachSource();
            UpdateTarget();
            return;
        }

        // 模板根先建好绑定、后拿到 TemplatedParent；模板字段变化时必须重挂一次源，TemplateBinding 才不会永远为空。
        if (ReferenceEquals(e.Property, VisualElement.TemplatedParentProperty))
        {
            if (_binding.Mode == BindingMode.OneTime)
            {
                return;
            }

            AttachSource();
            UpdateTarget();
            return;
        }

        if (ReferenceEquals(e.Property, _property) && _binding.Mode == BindingMode.TwoWay && !_updatingTarget)
        {
            UpdateSource(e.NewValue);
        }
    }

    private void UpdateTarget()
    {
        if (_disposed || _updatingTarget)
        {
            return;
        }

        var source = ResolveSource();
        if (source is null)
        {
            ApplyMissingSource();
            return;
        }

        if (!_path.TryResolve(source, out var value, out var nodes, out var leaf))
        {
            ApplyMissingSource();
            return;
        }

        _leaf = leaf;
        if (_binding.Mode != BindingMode.OneTime)
        {
            TrackNodes(nodes);
        }

        if (value is null && _binding.TargetNullValue is not null)
        {
            value = _binding.TargetNullValue;
        }

        if (_binding.Converter is { } converter)
        {
            value = converter is IParameterizedValueConverter parameterized
                ? parameterized.Convert(value, _property.PropertyType, _binding.ConverterParameter)
                : converter.Convert(value, _property.PropertyType);
        }

        if (!string.IsNullOrEmpty(_binding.StringFormat) && value is not null)
        {
            var format = _binding.StringFormat!.IndexOf('{') >= 0
                ? _binding.StringFormat!
                : "{0:" + _binding.StringFormat + "}";
            value = string.Format(CultureInfo.CurrentCulture, format, value);
        }

        ApplyTarget(value);
    }

    // 无源且没有显式 FallbackValue 时回到目标默认值；不要写一个 null 覆盖默认值。
    private void ApplyMissingSource()
    {
        if (_binding.FallbackValue is null)
        {
            _target.ClearValue(_property);
            return;
        }

        ApplyTarget(_binding.FallbackValue);
    }

    private void TrackNodes(List<object?> nodes)
    {
        foreach (var node in _nodes)
        {
            node.PropertyChanged -= OnSourcePropertyChanged;
        }

        _nodes.Clear();
        foreach (var node in nodes)
        {
            if (node is INotifyPropertyChanged observable && !_nodes.Contains(observable))
            {
                _nodes.Add(observable);
                observable.PropertyChanged += OnSourcePropertyChanged;
            }
        }
    }

    private void ApplyTarget(object? value)
    {
        _updatingTarget = true;
        try
        {
            // A null DataContext binding must not create a local null: that blocks the inherited context.
            if (value is null && ReferenceEquals(_property, VisualElement.DataContextProperty))
            {
                _target.ClearValue(_property);
                return;
            }

            if (value is null && _property.PropertyType.IsValueType && Nullable.GetUnderlyingType(_property.PropertyType) is null)
            {
                _target.ClearValue(_property);
                return;
            }

            var converted = _property.PropertyType.IsInstanceOfType(value)
                ? value
                : XamlRuntime.ConvertValue(value, _property.PropertyType);
            _target.SetValue(_property, converted);
        }
        finally
        {
            _updatingTarget = false;
        }
    }

    private void UpdateSource(object? value)
    {
        if (_leaf is not { CanWrite: true } leaf || _updatingSource)
        {
            return;
        }

        _updatingSource = true;
        try
        {
            if (_binding.Converter is { } converter)
            {
                value = converter is IParameterizedValueConverter parameterized
                    ? parameterized.ConvertBack(value, leaf.ValueType, _binding.ConverterParameter)
                    : converter.ConvertBack(value, leaf.ValueType);
            }

            leaf.SetValue(value);
        }
        finally
        {
            _updatingSource = false;
        }
    }
}
