using System.ComponentModel;
using System.Globalization;
using VeloxDev.WinForms.Xaml.Visuals;

namespace VeloxDev.WinForms.Xaml;

/// <summary>Path bindings for visual-backend objects; the source syntax is the existing WPF-style <see cref="Binding"/>.</summary>
/// <remarks>
/// <para>
/// The HWND backend uses <see cref="BindingExpression"/> because its targets are <c>System.Windows.Forms.Control</c>.
/// Visual elements implement <see cref="IWinformsObject"/> instead, so this expression resolves the same
/// <see cref="Binding.Path"/> against the effective <see cref="VisualElement.DataContext"/> and writes the target
/// through the priority store.
/// </para>
/// <para>
/// Supported: <see cref="BindingMode.OneWay"/>/<see cref="BindingMode.OneTime"/>/<see cref="BindingMode.TwoWay"/>,
/// simple dot paths with indexers, <see cref="Binding.Source"/>, converters, <see cref="Binding.StringFormat"/>,
/// <see cref="Binding.FallbackValue"/> and <see cref="Binding.TargetNullValue"/>. <see cref="Binding.ElementName"/>
/// and <see cref="Binding.RelativeSource"/> are not part of the first visual binding version.
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

        DetachSource();
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

        if (_binding.RelativeSource is { Mode: RelativeSourceMode.Self })
        {
            return _target;
        }

        return (_target as VisualElement)?.DataContext;
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
            ApplyTarget(_binding.FallbackValue);
            return;
        }

        if (!_path.TryResolve(source, out var value, out var nodes, out var leaf))
        {
            ApplyTarget(_binding.FallbackValue);
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
