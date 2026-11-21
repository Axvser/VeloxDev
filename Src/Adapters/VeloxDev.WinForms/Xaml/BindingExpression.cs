using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;

namespace VeloxDev.WinForms.Xaml;

/// <summary>Receives the value a <see cref="BindingExpression"/> resolves.</summary>
internal interface IBindingValueSink
{
    Type ValueType { get; }

    bool CanWriteBack { get; }

    object? GetValue();

    void SetValue(object? value);
}

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
    private readonly DependencyProperty? _property;
    private readonly Binding _definition;
    private readonly IBindingValueSink _sink;
    private readonly BindingPath _path;
    private readonly List<INotifyPropertyChanged> _observedProperties = [];
    private readonly List<INotifyCollectionChanged> _observedCollections = [];
    private readonly List<VeloxDev.WinForms.Xaml.Controls.Control> _observedControls = [];
    private readonly List<INotifyDataErrorInfo> _observedErrorSources = [];

    private NameScope? _nameScope;
    private object? _source;
    private bool _attached;
    private bool _applying;
    private bool _lostFocusHooked;
    private bool _disposed;

    public BindingExpression(VeloxDev.WinForms.Xaml.Controls.Control owner, DependencyProperty property, Binding definition)
        : this(owner, owner, property, definition)
    {
    }

    /// <summary>Creates an expression whose value target is any control, not only the data-context host.</summary>
    public BindingExpression(
        VeloxDev.WinForms.Xaml.Controls.Control owner,
        System.Windows.Forms.Control target,
        DependencyProperty property,
        Binding definition)
        : this(owner, definition, new DependencyBindingSink(target, property))
    {
        _target = target;
        _property = property;
    }

    /// <summary>Creates an expression that writes into a caller-supplied value layer.</summary>
    internal BindingExpression(
        VeloxDev.WinForms.Xaml.Controls.Control owner,
        Binding definition,
        IBindingValueSink sink)
    {
        _owner = owner;
        _target = owner;
        _definition = definition;
        _sink = sink;
        _path = new BindingPath(definition.Path);
    }

    /// <summary>The control receiving the bound value.</summary>
    public System.Windows.Forms.Control Target => _target;

    /// <summary>The dependency property receiving the bound value, when the target is a dependency property.</summary>
    public DependencyProperty? Property => _property;

    internal Binding Definition => _definition;

    /// <summary>Attaches to the current source, or stays idle while that source cannot be resolved yet.</summary>
    public void Attach()
    {
        if (_disposed)
        {
            return;
        }

        if (!TryResolveSource(out var source))
        {
            DetachSource();
            if (!string.IsNullOrEmpty(_definition.ElementName)
                && NameScope.Find(_owner) is { } pendingScope)
            {
                SubscribeNameScope(pendingScope);
            }

            ApplyFallback();
            return;
        }

        if (_attached && ReferenceEquals(source, _source))
        {
            return;
        }

        DetachSource();
        _source = source;
        _attached = true;
        HookLostFocus();
        ApplyToTarget();
    }

    /// <summary>Re-reads the source and rebuilds the subscription.</summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        DetachSource();
        Attach();
    }

    /// <summary>Writes the current target value back to the source.</summary>
    /// <remarks>This is the manual path for <see cref="UpdateSourceTrigger.Explicit"/>; other modes call it when their trigger fires.</remarks>
    public void UpdateSource()
    {
        if (_disposed || _source is null)
        {
            return;
        }

        WriteBack(_sink.GetValue());
    }

    /// <summary>Called after the target dependency property changed; writes back for a two-way binding.</summary>
    public void OnTargetValueChanged(object? oldValue, object? newValue, XamlValueSource source)
    {
        if (_disposed)
        {
            return;
        }

        // 作者/控件直接改目标值也要校验；绑定自己推进来的值在 ApplyToTarget 里已经校验过。
        if (source == XamlValueSource.Local && !_applying)
        {
            RunValidation(newValue, nodes: null);
        }

        if (_definition.Mode != BindingMode.TwoWay
            || source != XamlValueSource.Local
            || _applying
            || _source is null
            || !_sink.CanWriteBack)
        {
            return;
        }

        if (GetUpdateTrigger() == UpdateSourceTrigger.LostFocus
            || GetUpdateTrigger() == UpdateSourceTrigger.Explicit)
        {
            return;
        }

        WriteBack(newValue);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Validation.ClearErrors(_target, this);
        DetachSource();
    }

    private bool TryResolveSource(out object? source)
    {
        source = null;

        if (_definition.Source is not null)
        {
            source = _definition.Source;
            return true;
        }

        if (!string.IsNullOrEmpty(_definition.ElementName))
        {
            var scope = NameScope.Find(_owner);
            if (scope is null)
            {
                return false;
            }

            if (scope.TryFind(_definition.ElementName!, out var named))
            {
                source = named;
                return named is not null;
            }

            return false;
        }

        if (_definition.RelativeSource is { } relative)
        {
            source = ResolveRelativeSource(relative);
            return source is not null;
        }

        source = _owner.DataContext;
        return true;
    }

    private object? ResolveRelativeSource(RelativeSource relative)
    {
        switch (relative.Mode)
        {
            case RelativeSourceMode.Self:
                return _owner;

            case RelativeSourceMode.TemplatedParent:
                return _owner.TemplatedParent;

            case RelativeSourceMode.FindAncestor:
                if (relative.AncestorType is null)
                {
                    return null;
                }

                var level = Math.Max(1, relative.AncestorLevel);
                for (System.Windows.Forms.Control? current = _owner.Parent; current is not null; current = current.Parent)
                {
                    if (!relative.AncestorType.IsInstanceOfType(current))
                    {
                        continue;
                    }

                    if (--level == 0)
                    {
                        return current;
                    }
                }

                return null;

            default:
                return null;
        }
    }

    private void ApplyToTarget()
    {
        if (_disposed || _target.IsDisposed || _target.Disposing)
        {
            Observe(nodes: null);
            return;
        }

        if (_source is null)
        {
            Observe(nodes: null);
            ApplyFallback();
            return;
        }

        if (!_path.TryResolve(_source, out var value, out var nodes, out _))
        {
            Observe(nodes: null);
            ApplyFallback();
            return;
        }

        Observe(nodes);

        if (value is null && _definition.TargetNullValue is not null)
        {
            value = _definition.TargetNullValue;
        }

        var converted = ConvertForward(value);

        if (!Equals(_sink.GetValue(), converted))
        {
            _applying = true;
            try
            {
                _sink.SetValue(converted);
            }
            finally
            {
                _applying = false;
            }
        }

        // 值已经写进去了；校验只负责标记，不改变这次写入的结果。
        RunValidation(converted, nodes);

        if (_definition.Mode == BindingMode.OneTime)
        {
            // OneTime 每个源只推一次；之后换来源会自然重挂。
            DetachSource();
        }
    }

    private void ApplyFallback()
    {
        if (_disposed || _target.IsDisposed || _target.Disposing)
        {
            return;
        }

        var converted = ConvertForward(_definition.FallbackValue);
        if (!Equals(_sink.GetValue(), converted))
        {
            _applying = true;
            try
            {
                _sink.SetValue(converted);
            }
            finally
            {
                _applying = false;
            }
        }

        RunValidation(converted, nodes: null);
    }

    private object? ConvertForward(object? value)
    {
        if (_definition.Converter is { } converter)
        {
            value = converter is IParameterizedValueConverter parameterized
                ? parameterized.Convert(value, _sink.ValueType, _definition.ConverterParameter)
                : converter.Convert(value, _sink.ValueType);
        }

        if (!string.IsNullOrEmpty(_definition.StringFormat) && value is not null)
        {
            value = ApplyStringFormat(value, _definition.StringFormat!);
        }

        return XamlRuntime.ConvertValue(value, _sink.ValueType);
    }

    private void RunValidation(object? value, List<object?>? nodes)
    {
        if (_disposed)
        {
            return;
        }

        var errors = new List<ValidationError>();
        var rules = _definition.ValidationRules;
        for (var index = 0; index < rules.Count; index++)
        {
            var rule = rules[index];
            if (rule is null)
            {
                continue;
            }

            try
            {
                var result = rule.Validate(value, CultureInfo.CurrentCulture);
                if (result is not null && !result.IsValid)
                {
                    errors.Add(new ValidationError(rule, result.ErrorContent));
                }
            }
            catch (Exception exception)
            {
                errors.Add(new ValidationError(rule, exception));
            }
        }

        CollectSourceErrors(nodes, errors);
        Validation.PublishErrors(_target, this, errors, _definition.NotifyOnValidationError);
    }

    private void CollectSourceErrors(List<object?>? nodes, List<ValidationError> errors)
    {
        if (!_definition.ValidatesOnNotifyDataErrors && !_definition.ValidatesOnDataErrors)
        {
            return;
        }

        var propertyName = ValidationPropertyName();
        if (_source is not null)
        {
            AddSourceErrors(_source, propertyName, errors);
        }

        if (nodes is null)
        {
            return;
        }

        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            if (node is not null && !ReferenceEquals(node, _source))
            {
                AddSourceErrors(node, propertyName, errors);
            }
        }
    }

    private void AddSourceErrors(object source, string propertyName, List<ValidationError> errors)
    {
        // INotifyDataErrorInfo 优先于 IDataErrorInfo，避免同一对象两套接口各报一次。
        if (_definition.ValidatesOnNotifyDataErrors && source is INotifyDataErrorInfo notify)
        {
            try
            {
                var reported = notify.GetErrors(propertyName);
                if (reported is not null)
                {
                    foreach (var error in reported)
                    {
                        errors.Add(new ValidationError(null, error));
                    }
                }
            }
            catch (Exception exception)
            {
                errors.Add(new ValidationError(null, exception));
            }

            return;
        }

        if (!_definition.ValidatesOnDataErrors || source is not IDataErrorInfo data)
        {
            return;
        }

        string? message = null;
        try
        {
            message = propertyName.Length > 0 ? data[propertyName] : data.Error;
            if (string.IsNullOrEmpty(message) && propertyName.Length > 0)
            {
                message = data.Error;
            }
        }
        catch (Exception exception)
        {
            message = exception.Message;
        }

        if (!string.IsNullOrEmpty(message))
        {
            errors.Add(new ValidationError(null, message));
        }
    }

    private string ValidationPropertyName()
    {
        var path = _definition.Path;
        if (string.IsNullOrEmpty(path) || path == ".")
        {
            return string.Empty;
        }

        var segment = path.Substring(path.LastIndexOf('.') + 1);
        var open = segment.IndexOf('[');
        return open >= 0 ? segment.Substring(0, open) : segment;
    }

    private static string ApplyStringFormat(object value, string format)
    {
        // WPF accepts both "{0:N2}" and a bare "N2"; the bare form is shorthand for "{0:N2}".
        var composite = format.IndexOf('{') >= 0 ? format : "{0:" + format + "}";
        return string.Format(CultureInfo.CurrentCulture, composite, value);
    }

    private void WriteBack(object? value)
    {
        if (_source is null || !_path.TryResolve(_source, out var current, out _, out var leaf) || leaf is null || !leaf.CanWrite)
        {
            return;
        }

        object? converted;
        try
        {
            if (_definition.Converter is { } converter)
            {
                converted = converter is IParameterizedValueConverter parameterized
                    ? parameterized.ConvertBack(value, leaf.ValueType, _definition.ConverterParameter)
                    : converter.ConvertBack(value, leaf.ValueType);
            }
            else
            {
                converted = XamlRuntime.ConvertValue(value, leaf.ValueType);
            }
        }
        catch (Exception exception)
        {
            if (!_definition.ValidatesOnExceptions)
            {
                throw;
            }

            PublishException(exception);
            return;
        }

        if (Equals(current, converted))
        {
            RunValidation(converted, nodes: null);
            return;
        }

        _applying = true;
        try
        {
            leaf.SetValue(converted);
        }
        catch (Exception exception)
        {
            if (!_definition.ValidatesOnExceptions)
            {
                throw;
            }

            PublishException(exception);
            return;
        }
        finally
        {
            _applying = false;
        }

        RunValidation(converted, nodes: null);
    }

    private void PublishException(Exception exception)
        => Validation.PublishErrors(
            _target,
            this,
            new[] { new ValidationError(null, exception) },
            _definition.NotifyOnValidationError);

    private UpdateSourceTrigger GetUpdateTrigger()
        => _definition.UpdateSourceTrigger == UpdateSourceTrigger.Default
            ? UpdateSourceTrigger.PropertyChanged
            : _definition.UpdateSourceTrigger;

    private void HookLostFocus()
    {
        if (_lostFocusHooked || GetUpdateTrigger() != UpdateSourceTrigger.LostFocus)
        {
            return;
        }

        _target.LostFocus += OnTargetLostFocus;
        _lostFocusHooked = true;
    }

    private void OnTargetLostFocus(object? sender, EventArgs e) => UpdateSource();

    private void SubscribeNameScope(NameScope scope)
    {
        if (ReferenceEquals(_nameScope, scope))
        {
            return;
        }

        DetachNameScope();
        _nameScope = scope;
        scope.Registered += OnNameScopeRegistered;
    }

    private void OnNameScopeRegistered(object? sender, EventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (_nameScope is not null && _nameScope.TryFind(_definition.ElementName!, out _))
        {
            Refresh();
        }
    }

    private void DetachNameScope()
    {
        if (_nameScope is not null)
        {
            _nameScope.Registered -= OnNameScopeRegistered;
            _nameScope = null;
        }
    }

    private void Observe(List<object?>? nodes)
    {
        var nextProperties = new List<INotifyPropertyChanged>();
        var nextCollections = new List<INotifyCollectionChanged>();
        var nextControls = new List<VeloxDev.WinForms.Xaml.Controls.Control>();
        var nextErrors = new List<INotifyDataErrorInfo>();
        if (nodes is not null)
        {
            foreach (var node in nodes)
            {
                if (node is INotifyPropertyChanged observable
                    && !nextProperties.Exists(existing => ReferenceEquals(existing, observable)))
                {
                    nextProperties.Add(observable);
                }

                if (node is INotifyCollectionChanged collection
                    && !nextCollections.Exists(existing => ReferenceEquals(existing, collection)))
                {
                    nextCollections.Add(collection);
                }

                if (node is VeloxDev.WinForms.Xaml.Controls.Control control
                    && !nextControls.Exists(existing => ReferenceEquals(existing, control)))
                {
                    nextControls.Add(control);
                }

                if (node is INotifyDataErrorInfo errors
                    && !nextErrors.Exists(existing => ReferenceEquals(existing, errors)))
                {
                    nextErrors.Add(errors);
                }
            }
        }

        foreach (var previous in _observedProperties)
        {
            if (!nextProperties.Exists(candidate => ReferenceEquals(candidate, previous)))
            {
                previous.PropertyChanged -= OnSourcePropertyChanged;
            }
        }

        foreach (var previous in _observedCollections)
        {
            if (!nextCollections.Exists(candidate => ReferenceEquals(candidate, previous)))
            {
                previous.CollectionChanged -= OnSourceCollectionChanged;
            }
        }

        foreach (var previous in _observedControls)
        {
            if (!nextControls.Exists(candidate => ReferenceEquals(candidate, previous)))
            {
                previous.DependencyPropertyChanged -= OnSourceControlPropertyChanged;
            }
        }

        foreach (var previous in _observedErrorSources)
        {
            if (!nextErrors.Exists(candidate => ReferenceEquals(candidate, previous)))
            {
                previous.ErrorsChanged -= OnSourceErrorsChanged;
            }
        }

        foreach (var candidate in nextErrors)
        {
            if (!_observedErrorSources.Exists(previous => ReferenceEquals(previous, candidate)))
            {
                candidate.ErrorsChanged += OnSourceErrorsChanged;
            }
        }

        foreach (var candidate in nextControls)
        {
            if (!_observedControls.Exists(previous => ReferenceEquals(previous, candidate)))
            {
                candidate.DependencyPropertyChanged += OnSourceControlPropertyChanged;
            }
        }

        foreach (var candidate in nextProperties)
        {
            if (!_observedProperties.Exists(previous => ReferenceEquals(previous, candidate)))
            {
                candidate.PropertyChanged += OnSourcePropertyChanged;
            }
        }

        foreach (var candidate in nextCollections)
        {
            if (!_observedCollections.Exists(previous => ReferenceEquals(previous, candidate)))
            {
                candidate.CollectionChanged += OnSourceCollectionChanged;
            }
        }

        _observedProperties.Clear();
        _observedProperties.AddRange(nextProperties);
        _observedCollections.Clear();
        _observedCollections.AddRange(nextCollections);
        _observedControls.Clear();
        _observedControls.AddRange(nextControls);
        _observedErrorSources.Clear();
        _observedErrorSources.AddRange(nextErrors);
    }

    private void OnSourceControlPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // 只认路径第一段对应的依赖属性：模板触发器可能在同一次求值里改同一控件的另一个属性，
        // 无条件重放会让 TemplateBinding 把它覆盖回去。
        var first = _path.FirstSegment;
        if (first is null || string.Equals(first, e.Property.Name, StringComparison.Ordinal))
        {
            ApplyToTarget();
        }
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

    private void OnSourceCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        ApplyToTarget();
    }

    private void OnSourceErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        ApplyToTarget();
    }

    private void DetachSource()
    {
        foreach (var observable in _observedProperties)
        {
            observable.PropertyChanged -= OnSourcePropertyChanged;
        }

        foreach (var collection in _observedCollections)
        {
            collection.CollectionChanged -= OnSourceCollectionChanged;
        }

        foreach (var control in _observedControls)
        {
            control.DependencyPropertyChanged -= OnSourceControlPropertyChanged;
        }

        foreach (var errors in _observedErrorSources)
        {
            errors.ErrorsChanged -= OnSourceErrorsChanged;
        }

        _observedProperties.Clear();
        _observedCollections.Clear();
        _observedControls.Clear();
        _observedErrorSources.Clear();

        if (_lostFocusHooked)
        {
            _target.LostFocus -= OnTargetLostFocus;
            _lostFocusHooked = false;
        }

        DetachNameScope();
        _source = null;
        _attached = false;
    }
}

/// <summary>A sink for a plain CLR property on an object that is not a WinForms or XAML control.</summary>
internal sealed class PropertyBindingSink : IBindingValueSink
{
    private readonly object _target;
    private readonly PropertyDescriptor _property;

    public PropertyBindingSink(object target, string propertyName)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _property = TypeDescriptor.GetProperties(target).Find(propertyName, ignoreCase: false)
            ?? throw new InvalidOperationException(
                $"'{target.GetType()}' has no property named '{propertyName}'.");
    }

    public Type ValueType => _property.PropertyType;

    public bool CanWriteBack => false;

    public object? GetValue() => _property.GetValue(_target);

    public void SetValue(object? value)
    {
        if (!_property.IsReadOnly)
        {
            _property.SetValue(_target, XamlRuntime.ConvertValue(value, _property.PropertyType));
        }
    }
}

/// <summary>The default sink: one dependency property on one control.</summary>
internal sealed class DependencyBindingSink : IBindingValueSink
{
    private readonly System.Windows.Forms.Control _target;
    private readonly DependencyProperty _property;

    public DependencyBindingSink(System.Windows.Forms.Control target, DependencyProperty property)
    {
        _target = target;
        _property = property;
    }

    public Type ValueType => _property.PropertyType;

    public bool CanWriteBack => true;

    public object? GetValue() => _property.GetValue(_target);

    public void SetValue(object? value)
    {
        // A Control target goes through the change pipeline so triggers and the derived control see the bound
        // value exactly as they see a local write. A native control has no such pipeline.
        if (_target is VeloxDev.WinForms.Xaml.Controls.Control xaml)
        {
            _property.SetValueCore(xaml, value, XamlValueSource.Binding);
        }
        else
        {
            _property.SetValue(_target, value);
        }
    }
}
