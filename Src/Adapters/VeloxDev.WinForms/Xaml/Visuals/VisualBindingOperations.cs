using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
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
/// Supported: <see cref="BindingMode.OneWay"/>/<see cref="BindingMode.OneTime"/>/<see cref="BindingMode.TwoWay"/>/
/// <see cref="BindingMode.OneWayToSource"/>, simple dot paths with indexers, <see cref="Binding.Source"/>, converters,
/// <see cref="Binding.StringFormat"/>, <see cref="Binding.FallbackValue"/> and <see cref="Binding.TargetNullValue"/>.
/// <see cref="Binding.ElementName"/> resolves a named element in the target's namescope (ancestor or not);
/// <see cref="Binding.RelativeSource"/> supports <c>Self</c>, <c>TemplatedParent</c> and <c>FindAncestor</c> with
/// <see cref="RelativeSource.AncestorLevel"/>.
/// </para>
/// <para>
/// A two-way or one-way-to-source binding writes back under <see cref="Binding.UpdateSourceTrigger"/>: the default and
/// <see cref="UpdateSourceTrigger.PropertyChanged"/> write on every target change,
/// <see cref="UpdateSourceTrigger.LostFocus"/> writes when the target element loses focus, and
/// <see cref="UpdateSourceTrigger.Explicit"/> never writes without an <see cref="IVisualBindingExpression.UpdateSource"/>
/// call.
/// </para>
/// </remarks>
public static class VisualBindingOperations
{
    // 生成代码会丢弃 Bind 的返回值；XAML 里的 UpdateSourceTrigger=Explicit 只能按 target+property 找回表达式。
    private static readonly ConditionalWeakTable<IWinformsObject, List<VisualBindingExpression>> ActiveExpressions = new();

    /// <summary>
    /// Raised when a binding cannot honor its configuration at runtime, for example a missing
    /// <c>FindAncestor</c> target or an unvalidated source-write exception.
    /// </summary>
    /// <remarks>Path misses and a missing data context are normal and do not raise this event.</remarks>
    public static event EventHandler<BindingFailureEventArgs>? BindingFailed;

    /// <summary>Binds <paramref name="property"/> on <paramref name="target"/> to <paramref name="binding"/>.</summary>
    /// <param name="target">The visual-backend object receiving values.</param>
    /// <param name="property">The property to write.</param>
    /// <param name="binding">The WPF-style binding description.</param>
    /// <returns>The live expression; disposing it detaches the binding.</returns>
    public static IVisualBindingExpression Bind(IWinformsObject target, DependencyProperty property, Binding binding)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (property is null) throw new ArgumentNullException(nameof(property));
        if (binding is null) throw new ArgumentNullException(nameof(binding));

        var expression = new VisualBindingExpression(target, property, binding);
        expression.Attach();

        var expressions = ActiveExpressions.GetOrCreateValue(target);
        lock (expressions)
        {
            expressions.Add(expression);
        }

        return expression;
    }

    /// <summary>
    /// Writes the target's current value back to the source for every live binding of
    /// <paramref name="property"/> on <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The target that owns the binding.</param>
    /// <param name="property">The bound property.</param>
    /// <returns><see langword="true"/> when at least one binding wrote a writable source path.</returns>
    /// <remarks>This is the only way to flush a binding created from XAML with <see cref="UpdateSourceTrigger.Explicit"/>.</remarks>
    public static bool UpdateSource(IWinformsObject target, DependencyProperty property)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (property is null) throw new ArgumentNullException(nameof(property));

        if (!ActiveExpressions.TryGetValue(target, out var expressions))
        {
            return false;
        }

        VisualBindingExpression[] snapshot;
        lock (expressions)
        {
            snapshot = [.. expressions];
        }

        var updated = false;
        foreach (var expression in snapshot)
        {
            if (ReferenceEquals(expression.Property, property) && expression.UpdateSource())
            {
                updated = true;
            }
        }

        return updated;
    }

    internal static void Untrack(VisualBindingExpression expression)
    {
        if (ActiveExpressions.TryGetValue(expression.Target, out var expressions))
        {
            lock (expressions)
            {
                expressions.Remove(expression);
            }
        }
    }

    internal static void ReportFailure(
        IWinformsObject target,
        DependencyProperty property,
        Binding binding,
        string message,
        Exception? exception = null)
    {
        BindingFailed?.Invoke(target, new BindingFailureEventArgs(target, property, binding, message, exception));
    }
}

/// <summary>Extension helpers for visual path bindings.</summary>
public static class VisualBindingExtensions
{
    /// <summary>Binds <paramref name="property"/> to a WPF-style <paramref name="binding"/>.</summary>
    /// <param name="target">The visual-backend object receiving values.</param>
    /// <param name="property">The property to write.</param>
    /// <param name="binding">The binding description.</param>
    /// <returns>The live expression; disposing it detaches the binding.</returns>
    public static IVisualBindingExpression SetBinding(this IWinformsObject target, DependencyProperty property, Binding binding)
        => VisualBindingOperations.Bind(target, property, binding);
}

/// <summary>A live binding created by <see cref="VisualBindingOperations.Bind"/>.</summary>
public interface IVisualBindingExpression : IDisposable
{
    /// <summary>
    /// Writes the target's current value back to the source, bypassing <see cref="UpdateSourceTrigger.Explicit"/>.
    /// </summary>
    /// <returns><see langword="true"/> when a writable source path received the value.</returns>
    bool UpdateSource();
}

/// <summary>Describes a binding that could not honor its configuration at runtime.</summary>
public sealed class BindingFailureEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="target">The element whose property is bound.</param>
    /// <param name="property">The bound property.</param>
    /// <param name="binding">The binding description.</param>
    /// <param name="message">A human-readable reason.</param>
    /// <param name="exception">The exception that caused the failure, when there is one.</param>
    public BindingFailureEventArgs(
        IWinformsObject target,
        DependencyProperty property,
        Binding binding,
        string message,
        Exception? exception = null)
    {
        Target = target;
        Property = property;
        Binding = binding;
        Message = message;
        Exception = exception;
    }

    /// <summary>The element whose property is bound.</summary>
    public IWinformsObject Target { get; }

    /// <summary>The bound property.</summary>
    public DependencyProperty Property { get; }

    /// <summary>The binding description.</summary>
    public Binding Binding { get; }

    /// <summary>A human-readable reason.</summary>
    public string Message { get; }

    /// <summary>The exception that caused the failure, when there is one.</summary>
    public Exception? Exception { get; }
}

/// <summary>One validation error contributed by a binding.</summary>
public sealed class ValidationError
{
    /// <summary>Creates a validation error.</summary>
    /// <param name="ruleInError">The rule or object that produced the error, or <see langword="null"/>.</param>
    /// <param name="errorContent">The error content, usually a message or an <see cref="Exception"/>.</param>
    public ValidationError(object? ruleInError, object? errorContent)
    {
        RuleInError = ruleInError;
        ErrorContent = errorContent;
    }

    /// <summary>The rule or object that produced the error, or <see langword="null"/>.</summary>
    public object? RuleInError { get; }

    /// <summary>The error content, usually a message or an <see cref="Exception"/>.</summary>
    public object? ErrorContent { get; }

    /// <summary>The content as an <see cref="Exception"/>, or <see langword="null"/> when it is not one.</summary>
    public Exception? Exception => ErrorContent as Exception;
}

/// <summary>Whether a <see cref="Validation.Error"/> event reports an added or a removed error.</summary>
public enum ValidationErrorEventAction
{
    /// <summary>The error was added.</summary>
    Added,

    /// <summary>The error was removed.</summary>
    Removed,
}

/// <summary>Data for <see cref="Validation.Error"/>.</summary>
public sealed class ValidationErrorEventArgs : EventArgs
{
    /// <summary>Creates the event data.</summary>
    /// <param name="error">The error that changed.</param>
    /// <param name="action">Whether the error was added or removed.</param>
    public ValidationErrorEventArgs(ValidationError error, ValidationErrorEventAction action)
    {
        Error = error;
        Action = action;
    }

    /// <summary>The error that changed.</summary>
    public ValidationError Error { get; }

    /// <summary>Whether the error was added or removed.</summary>
    public ValidationErrorEventAction Action { get; }

    /// <summary>The element that owns the error.</summary>
    public IWinformsObject? Source { get; internal set; }

    /// <summary>Whether another handler should ignore this event.</summary>
    public bool Handled { get; set; }
}

/// <summary>
/// The WPF-shaped validation surface for bindings: the attached <see cref="HasErrorProperty"/> and
/// <see cref="ErrorsProperty"/> state, and the <see cref="Error"/> event raised when
/// <see cref="Binding.NotifyOnValidationError"/> is set.
/// </summary>
/// <remarks>
/// <para>
/// This is the minimal surface, not the WPF rule API. Errors come from
/// <see cref="Binding.ValidatesOnExceptions"/>, <see cref="Binding.ValidatesOnNotifyDataErrors"/> and
/// <see cref="Binding.ValidatesOnDataErrors"/>; there is no <c>ValidationRule</c> collection.
/// </para>
/// <para>
/// State is stored per <see cref="IWinformsObject"/> through the priority store, so an element can carry the validation
/// state of more than one binding like it can in WPF.
/// </para>
/// <para>
/// <see cref="IDataErrorInfo"/> and <see cref="INotifyDataErrorInfo"/> errors are read from the binding's resolved
/// source object, keyed by the last path segment. A path such as <c>Child.Name</c> therefore reports errors on
/// <c>Child</c>'s owner rather than on <c>Child</c>.
/// </para>
/// </remarks>
public static class Validation
{
    /// <summary>True when the element currently carries at least one validation error.</summary>
    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.RegisterAttached(
        "HasError", typeof(bool), typeof(Validation),
        new PropertyMetadata(false));

    /// <summary>The current validation errors of the element, or <see langword="null"/>.</summary>
    public static readonly DependencyProperty ErrorsProperty = DependencyProperty.RegisterAttached(
        "Errors", typeof(IReadOnlyList<ValidationError>), typeof(Validation));

    /// <summary>Raised when a binding with <see cref="Binding.NotifyOnValidationError"/> adds or removes an error.</summary>
    public static event EventHandler<ValidationErrorEventArgs>? Error;

    /// <summary>Reads <see cref="HasErrorProperty"/> from <paramref name="element"/>.</summary>
    /// <param name="element">The element to inspect.</param>
    public static bool GetHasError(IWinformsObject element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return element.GetValue(HasErrorProperty) is true;
    }

    /// <summary>Reads <see cref="ErrorsProperty"/> from <paramref name="element"/>.</summary>
    /// <param name="element">The element to inspect.</param>
    public static IReadOnlyList<ValidationError>? GetErrors(IWinformsObject element)
    {
        if (element is null) throw new ArgumentNullException(nameof(element));
        return element.GetValue(ErrorsProperty) as IReadOnlyList<ValidationError>;
    }

    internal static void SetErrors(IWinformsObject element, IReadOnlyList<ValidationError>? errors)
    {
        var hasError = errors is { Count: > 0 };
        element.SetValue(ErrorsProperty, hasError ? errors : null);
        element.SetValue(HasErrorProperty, hasError);
    }

    internal static void RaiseError(IWinformsObject target, ValidationError error, ValidationErrorEventAction action)
    {
        var handler = Error;
        if (handler is null)
        {
            return;
        }

        handler(target, new ValidationErrorEventArgs(error, action) { Source = target });
    }
}

internal sealed class VisualBindingExpression : IVisualBindingExpression
{
    private readonly IWinformsObject _target;
    private readonly DependencyProperty _property;
    private readonly Binding _binding;
    private readonly BindingPath _path;
    private readonly string _validationPropertyName;
    private readonly bool _pushesTarget;
    private readonly bool _writesSource;
    private readonly List<INotifyPropertyChanged> _nodes = [];
    private VisualElement? _dataContextParent;
    private BindingLeaf? _leaf;
    private object? _resolvedSource;
    private INotifyDataErrorInfo? _notifyDataSource;
    private IReadOnlyList<ValidationError>? _validationErrors;
    private bool _pendingSourceUpdate;
    private bool _updatingTarget;
    private bool _updatingSource;
    private bool _disposed;

    public VisualBindingExpression(IWinformsObject target, DependencyProperty property, Binding binding)
    {
        _target = target;
        _property = property;
        _binding = binding;
        _path = new BindingPath(binding.Path);
        _validationPropertyName = LastPropertyName(binding.Path);
        _pushesTarget = binding.Mode is BindingMode.OneWay or BindingMode.TwoWay or BindingMode.OneTime;
        _writesSource = binding.Mode is BindingMode.TwoWay or BindingMode.OneWayToSource;
    }

    // Default 对依赖属性等价于 PropertyChanged，与本引擎改写前的行为一致。
    private UpdateSourceTrigger EffectiveTrigger
        => _binding.UpdateSourceTrigger == UpdateSourceTrigger.Default
            ? UpdateSourceTrigger.PropertyChanged
            : _binding.UpdateSourceTrigger;

    internal IWinformsObject Target => _target;

    internal DependencyProperty Property => _property;

    public void Attach()
    {
        _target.PropertyChanged += OnTargetPropertyChanged;

        AttachDataContextParent();

        // 绑定可能在挂上父级之前就建立（预构造子树、复用视图）；任何绑定都要在父级变化后重解源，
        // 否则 ElementName、FindAncestor 与继承来的 DataContext 会一直停在建立时看到的空源上。
        if (_target is VisualElement parentedTarget)
        {
            parentedTarget.ParentChanged += OnTargetParentChanged;
        }

        if (_writesSource)
        {
            switch (EffectiveTrigger)
            {
                case UpdateSourceTrigger.LostFocus when _target is VisualElement focusTarget:
                    focusTarget.LostFocus += OnTargetLostFocus;
                    break;

                case UpdateSourceTrigger.LostFocus:
                    VisualBindingOperations.ReportFailure(
                        _target,
                        _property,
                        _binding,
                        "UpdateSourceTrigger.LostFocus requires a VisualElement target; the source is never written.");
                    break;

                // Explicit 只响应 IVisualBindingExpression.UpdateSource，没有可订阅的触发点。
            }
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
        VisualBindingOperations.Untrack(this);
        _target.PropertyChanged -= OnTargetPropertyChanged;

        if (_target is VisualElement contextTarget)
        {
            contextTarget.ParentChanged -= OnTargetParentChanged;
            contextTarget.LostFocus -= OnTargetLostFocus;
        }

        DetachDataContextParent();
        UnsubscribeNotifyDataErrors();
        DetachNodes();

        if (_validationErrors is { Count: > 0 })
        {
            _validationErrors = null;
            Validation.SetErrors(_target, null);
        }
    }

    public bool UpdateSource()
    {
        if (_disposed || !_writesSource)
        {
            return false;
        }

        _pendingSourceUpdate = false;
        return TryWriteSource(_target.GetValue(_property));
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
        UpdateTarget();
    }

    private void OnDataContextParentChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (ReferenceEquals(e.Property, VisualElement.DataContextProperty))
        {
            UpdateTarget();
        }
    }

    private void DetachNodes()
    {
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

        if (_binding.RelativeSource is { } relative)
        {
            switch (relative.Mode)
            {
                case RelativeSourceMode.Self:
                    return _target;

                case RelativeSourceMode.TemplatedParent:
                    return (_target as VisualElement)?.TemplatedParent;

                case RelativeSourceMode.FindAncestor:
                    return ResolveAncestor(relative);
            }
        }

        // A binding whose target IS DataContext resolves against the inherited parent context. Reading the
        // target's own DataContext would re-evaluate the path against the value it just produced.
        var element = _target as VisualElement;
        return ReferenceEquals(_property, VisualElement.DataContextProperty)
            ? element?.ParentElement?.DataContext
            : element?.DataContext;
    }

    private object? ResolveAncestor(RelativeSource relative)
    {
        if (_target is not VisualElement element)
        {
            VisualBindingOperations.ReportFailure(
                _target,
                _property,
                _binding,
                "A FindAncestor source requires a VisualElement target.");
            return null;
        }

        if (relative.AncestorType is not { } ancestorType)
        {
            VisualBindingOperations.ReportFailure(
                _target,
                _property,
                _binding,
                "A FindAncestor source requires RelativeSource.AncestorType.");
            return null;
        }

        var level = Math.Max(1, relative.AncestorLevel);
        var remaining = level;

        var start = element.ParentElement ?? element.TemplatedParent;
        if (start is null)
        {
            // 还没进树：祖先查询尚无意义，父级变化后会重解；此刻只落到 FallbackValue，不当作失败。
            return null;
        }

        // 模板部件的逻辑父级记在 TemplatedParent 上；没挂进视觉树时它是唯一的父边。
        var visited = new HashSet<VisualElement>();
        for (var current = start; current is not null; current = NextAncestor(current))
        {
            if (!visited.Add(current))
            {
                break;
            }

            if (ancestorType.IsInstanceOfType(current) && --remaining == 0)
            {
                return current;
            }
        }

        VisualBindingOperations.ReportFailure(
            _target,
            _property,
            _binding,
            $"No ancestor of type '{ancestorType.FullName}' was found at AncestorLevel {level.ToString(CultureInfo.InvariantCulture)}.");

        return null;

        static VisualElement? NextAncestor(VisualElement current)
            => current.ParentElement ?? current.TemplatedParent;
    }

    private static VisualElement? FindNamedElement(VisualElement target, string name)
    {
        // 1. 最近的模板实例名字域：同一 item/模板实例的部件优先，绝不跨实例。
        if (target.FindInEnclosingNameScope(name) as VisualElement is { } scoped)
        {
            return scoped;
        }

        // 2. 模板外的命名元素通常是祖先（例如工作流模板按 ElementName 找 PART_Canvas），按名字查祖先。
        for (var current = target.ParentElement; current is not null; current = current.ParentElement)
        {
            if (string.Equals(current.Name, name, StringComparison.Ordinal))
            {
                return current;
            }
        }

        // 3. 模板实例内部到此为止：全树遍历会把绑定串到另一个 item 的同名部件。
        if (target.EnclosingNameScope is not null)
        {
            return null;
        }

        // 4. 非模板树回退到从根开始的深度优先查找（兄弟元素、页面里任意命名元素）。
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

    private void OnTargetLostFocus(object? sender, EventArgs e)
    {
        if (_pendingSourceUpdate)
        {
            UpdateSource();
        }
    }

    private void OnTargetPropertyChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (ReferenceEquals(e.Property, VisualElement.DataContextProperty)
            || ReferenceEquals(e.Property, VisualElement.TemplatedParentProperty))
        {
            if (_binding.Mode == BindingMode.OneTime)
            {
                return;
            }

            // 模板根先建好绑定、后拿到 TemplatedParent；源变了就必须重挂一次，否则会停在空源上。
            UpdateTarget();
            return;
        }

        if (!_writesSource || !ReferenceEquals(e.Property, _property) || _updatingTarget)
        {
            return;
        }

        if (EffectiveTrigger == UpdateSourceTrigger.PropertyChanged)
        {
            TryWriteSource(e.NewValue);
        }
        else
        {
            // LostFocus 与 Explicit 都推迟到各自的触发点，目标变化本身不写源。
            _pendingSourceUpdate = true;
        }
    }

    private void UpdateTarget()
    {
        if (_disposed || _updatingTarget)
        {
            return;
        }

        object? value = null;
        try
        {
            var source = ResolveSource();
            if (source is null)
            {
                _resolvedSource = null;
                DetachNodes();
                ClearValidationError();
                if (_pushesTarget)
                {
                    ApplyMissingSource();
                }

                return;
            }

            if (!_path.TryResolve(source, out value, out var nodes, out var leaf))
            {
                _resolvedSource = source;
                DetachNodes();
                UpdateValidationDataErrors(source);
                if (_pushesTarget)
                {
                    ApplyMissingSource();
                }

                return;
            }

            _resolvedSource = source;
            _leaf = leaf;
            if (value is null && _binding.TargetNullValue is not null)
            {
                value = _binding.TargetNullValue;
            }

            if (_binding.Mode != BindingMode.OneTime)
            {
                TrackNodes(nodes);
            }

            UpdateValidationDataErrors(source);
        }
        catch (Exception ex) when (_binding.ValidatesOnExceptions)
        {
            SetValidationError(ex);
            if (_pushesTarget)
            {
                ApplyMissingSource();
            }

            return;
        }
        catch (Exception ex)
        {
            VisualBindingOperations.ReportFailure(
                _target,
                _property,
                _binding,
                "Reading or converting the binding source threw.",
                Unwrap(ex));
            if (_pushesTarget)
            {
                ApplyMissingSource();
            }

            return;
        }

        // OneWayToSource 只准备可写叶子和校验状态，绝不把源值推给目标。
        if (!_pushesTarget)
        {
            return;
        }

        try
        {
            value = FormatTarget(ConvertToTarget(value));
            ApplyTarget(value);
        }
        catch (Exception ex) when (_binding.ValidatesOnExceptions)
        {
            SetValidationError(ex);
        }
        catch (Exception ex)
        {
            VisualBindingOperations.ReportFailure(
                _target,
                _property,
                _binding,
                "Converting or assigning the target value threw.",
                Unwrap(ex));
            ApplyMissingSource();
        }
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

    private bool TryWriteSource(object? value)
    {
        if (_disposed || !_writesSource)
        {
            return false;
        }

        if (_leaf is not { CanWrite: true } leaf || _updatingSource)
        {
            return false;
        }

        _updatingSource = true;
        try
        {
            try
            {
                leaf.SetValue(ConvertToSource(value, leaf.ValueType));
            }
            catch (Exception ex) when (_binding.ValidatesOnExceptions)
            {
                // 失败保持目标值不动，源不被写入；HasError/Errors 让视图能画出错误。
                SetValidationError(ex);
                return false;
            }
            catch (Exception ex)
            {
                VisualBindingOperations.ReportFailure(
                    _target,
                    _property,
                    _binding,
                    "Writing the binding source threw.",
                    Unwrap(ex));
                return false;
            }

            ClearValidationError();
            UpdateValidationDataErrors(_resolvedSource);
            return true;
        }
        finally
        {
            _updatingSource = false;
        }
    }

    private object? FormatTarget(object? value)
    {
        if (string.IsNullOrEmpty(_binding.StringFormat) || value is null)
        {
            return value;
        }

        var format = _binding.StringFormat!.IndexOf('{') >= 0
            ? _binding.StringFormat!
            : "{0:" + _binding.StringFormat + "}";
        return string.Format(CultureInfo.CurrentCulture, format, value);
    }

    private object? ConvertToTarget(object? value)
    {
        if (_binding.Converter is not { } converter)
        {
            return value;
        }

        var culture = _binding.ConverterCulture ?? CultureInfo.CurrentCulture;
        return converter switch
        {
            ICultureAwareValueConverter cultureAware => cultureAware.Convert(
                value,
                _property.PropertyType,
                _binding.ConverterParameter,
                culture),
            IParameterizedValueConverter parameterized => parameterized.Convert(
                value,
                _property.PropertyType,
                _binding.ConverterParameter),
            _ => converter.Convert(value, _property.PropertyType),
        };
    }

    private object? ConvertToSource(object? value, Type sourceType)
    {
        if (_binding.Converter is not { } converter)
        {
            return value;
        }

        var culture = _binding.ConverterCulture ?? CultureInfo.CurrentCulture;
        return converter switch
        {
            ICultureAwareValueConverter cultureAware => cultureAware.ConvertBack(
                value,
                sourceType,
                _binding.ConverterParameter,
                culture),
            IParameterizedValueConverter parameterized => parameterized.ConvertBack(
                value,
                sourceType,
                _binding.ConverterParameter),
            _ => converter.ConvertBack(value, sourceType),
        };
    }

    private void UpdateValidationDataErrors(object? source)
    {
        if (_binding.ValidatesOnNotifyDataErrors && source is INotifyDataErrorInfo notifyDataErrors)
        {
            SubscribeNotifyDataErrors(notifyDataErrors);
        }
        else
        {
            UnsubscribeNotifyDataErrors();
        }

        if (_binding.ValidatesOnDataErrors && source is IDataErrorInfo dataErrors)
        {
            string? message = null;
            try
            {
                message = dataErrors[_validationPropertyName];
            }
            catch (Exception)
            {
                // 旧式接口的索引器对未知属性名可能抛异常；没有错误内容就是没有错误。
            }

            if (!string.IsNullOrEmpty(message))
            {
                SetValidationErrors([new ValidationError(dataErrors, message)]);
                return;
            }
        }

        if (_binding.ValidatesOnNotifyDataErrors && source is INotifyDataErrorInfo notifySource)
        {
            var errors = new List<ValidationError>();
            try
            {
                if (notifySource.GetErrors(_validationPropertyName) is { } items)
                {
                    foreach (var item in items)
                    {
                        errors.Add(item as ValidationError ?? new ValidationError(notifySource, item));
                    }
                }
            }
            catch (Exception)
            {
                // GetErrors 抛异常等同于暂时没有错误；ErrorsChanged 会再触发一次求值。
            }

            if (errors.Count > 0)
            {
                SetValidationErrors(errors);
                return;
            }
        }

        ClearValidationError();
    }

    private void SubscribeNotifyDataErrors(INotifyDataErrorInfo source)
    {
        if (ReferenceEquals(_notifyDataSource, source))
        {
            return;
        }

        UnsubscribeNotifyDataErrors();
        _notifyDataSource = source;
        source.ErrorsChanged += OnNotifyDataErrorsChanged;
    }

    private void UnsubscribeNotifyDataErrors()
    {
        if (_notifyDataSource is { } source)
        {
            source.ErrorsChanged -= OnNotifyDataErrorsChanged;
            _notifyDataSource = null;
        }
    }

    private void OnNotifyDataErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        // 只有与本绑定同一属性（或整体）的错误变化才需要重推目标。
        if (string.IsNullOrEmpty(e.PropertyName)
            || string.Equals(e.PropertyName, _validationPropertyName, StringComparison.Ordinal))
        {
            UpdateTarget();
        }
    }

    private void SetValidationError(Exception exception)
        => SetValidationErrors([new ValidationError(null, Unwrap(exception))]);

    private static Exception Unwrap(Exception exception)
        // TypeDescriptor 把 setter 抛出的异常包成 TargetInvocationException；校验错误与事件应给出真实异常。
        => exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;

    private void ClearValidationError()
    {
        if (_validationErrors is { Count: > 0 })
        {
            SetValidationErrors(null);
        }
    }

    private void SetValidationErrors(IReadOnlyList<ValidationError>? errors)
    {
        var previous = _validationErrors;
        var hasError = errors is { Count: > 0 };
        _validationErrors = hasError ? errors : null;
        Validation.SetErrors(_target, _validationErrors);

        if (_disposed || !_binding.NotifyOnValidationError)
        {
            return;
        }

        // 错误集合内容变化但两轮都非空时不再报一次 Added，避免每次源通知都刷事件。
        if (hasError && previous is not { Count: > 0 })
        {
            foreach (var error in errors!)
            {
                Validation.RaiseError(_target, error, ValidationErrorEventAction.Added);
            }
        }
        else if (!hasError && previous is { Count: > 0 })
        {
            foreach (var error in previous)
            {
                Validation.RaiseError(_target, error, ValidationErrorEventAction.Removed);
            }
        }
    }

    // IDataErrorInfo/INotifyDataErrorInfo 以属性名为键；路径末段去掉索引器就是该名字。
    private static string LastPropertyName(string path)
    {
        if (string.IsNullOrEmpty(path) || path == ".")
        {
            return string.Empty;
        }

        var dot = path.LastIndexOf('.');
        var last = dot >= 0 ? path.Substring(dot + 1) : path;
        var bracket = last.IndexOf('[');
        return bracket >= 0 ? last.Substring(0, bracket) : last;
    }
}
