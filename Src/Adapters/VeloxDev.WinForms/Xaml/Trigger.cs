using System.Collections.ObjectModel;

namespace VeloxDev.WinForms.Xaml;

/// <summary>One property/value pair applied while a <see cref="Trigger"/> or <see cref="Style"/> is active.</summary>
public sealed class Setter
{
    private DependencyProperty? _property;
    private string? _propertyName;
    private object? _value;

    /// <summary>Creates an unconfigured setter.</summary>
    public Setter()
    {
    }

    /// <summary>Creates a setter for <paramref name="property"/>.</summary>
    /// <param name="property">The dependency property to set.</param>
    /// <param name="value">The value to apply.</param>
    public Setter(DependencyProperty property, object? value)
    {
        _property = property;
        _value = value;
    }

    /// <summary>Creates a setter for a native CLR property such as <c>Text</c> or <c>BackColor</c>.</summary>
    /// <param name="propertyName">The native property name.</param>
    /// <param name="value">The value to apply.</param>
    public Setter(string propertyName, object? value)
    {
        _propertyName = propertyName;
        _value = value;
    }

    /// <summary>Raised when a property of this setter changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The dependency property to set, or <see langword="null"/> for a native property setter.</summary>
    public DependencyProperty? Property
    {
        get => _property;
        set => SetField(ref _property, value);
    }

    /// <summary>The native CLR property name, or <see langword="null"/> for a dependency-property setter.</summary>
    public string? PropertyName
    {
        get => _propertyName;
        set => SetField(ref _propertyName, value);
    }

    /// <summary>The value applied while the owning trigger or style is active.</summary>
    public object? Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    private void SetField<T>(ref T field, T value)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>The setters a trigger applies while its condition holds.</summary>
public sealed class SetterCollection : Collection<Setter>
{
    /// <summary>Raised after an item is inserted, replaced, removed, cleared or changed.</summary>
    public event EventHandler? Changed;

    /// <inheritdoc />
    protected override void InsertItem(int index, Setter item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        base.InsertItem(index, item);
        item.Changed += OnItemChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override void SetItem(int index, Setter item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        this[index].Changed -= OnItemChanged;
        base.SetItem(index, item);
        item.Changed += OnItemChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override void RemoveItem(int index)
    {
        this[index].Changed -= OnItemChanged;
        base.RemoveItem(index);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override void ClearItems()
    {
        foreach (var item in this)
        {
            item.Changed -= OnItemChanged;
        }

        base.ClearItems();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// A condition on one dependency property. A normal <see cref="Trigger"/> and a <see cref="MultiTrigger"/> condition
/// share this shape; <see cref="DataTrigger"/> uses a binding instead.
/// </summary>
public sealed class Condition
{
    private DependencyProperty? _property;
    private object? _value;

    /// <summary>Creates an unconfigured condition.</summary>
    public Condition()
    {
    }

    /// <summary>Creates a condition on <paramref name="property"/>.</summary>
    /// <param name="property">The dependency property to observe.</param>
    /// <param name="value">The value that makes the condition hold.</param>
    public Condition(DependencyProperty property, object? value)
    {
        _property = property;
        _value = value;
    }

    /// <summary>Raised when a property of this condition changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The dependency property to observe.</summary>
    public DependencyProperty? Property
    {
        get => _property;
        set => SetField(ref _property, value);
    }

    /// <summary>The value that makes the condition hold.</summary>
    public object? Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    private void SetField<T>(ref T field, T value)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>The conditions of a <see cref="MultiTrigger"/>.</summary>
public sealed class ConditionCollection : Collection<Condition>
{
    /// <summary>Raised after an item is inserted, replaced, removed, cleared or changed.</summary>
    public event EventHandler? Changed;

    /// <inheritdoc />
    protected override void InsertItem(int index, Condition item)
    {
        base.InsertItem(index, item);
        item.Changed += OnItemChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override void SetItem(int index, Condition item)
    {
        this[index].Changed -= OnItemChanged;
        base.SetItem(index, item);
        item.Changed += OnItemChanged;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override void RemoveItem(int index)
    {
        this[index].Changed -= OnItemChanged;
        base.RemoveItem(index);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    protected override void ClearItems()
    {
        foreach (var item in this)
        {
            item.Changed -= OnItemChanged;
        }

        base.ClearItems();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void OnItemChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// A property/value condition evaluated against its owning <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/>. While the condition holds its
/// <see cref="Setters"/> are applied; when it stops holding the previous values are restored and the exit action runs.
/// </summary>
/// <remarks>
/// This is intentionally not WPF's styling system. There is no style, no template binding and no dependency between
/// triggers; one trigger watches one dependency property on the control it belongs to. <see cref="DataTrigger"/> and
/// <see cref="MultiTrigger"/> specialize the condition while reusing the setters and enter/exit actions.
/// </remarks>
public class Trigger
{
    private DependencyProperty? _property;
    private object? _value;
    private TriggerAction? _enterAction;
    private TriggerAction? _exitAction;

    /// <summary>Creates an unconfigured trigger.</summary>
    public Trigger()
    {
        Setters.Changed += OnSettersChanged;
    }

    /// <summary>Creates a trigger that is active when <paramref name="property"/> equals <see langword="null"/>.</summary>
    /// <param name="property">The dependency property to observe.</param>
    public Trigger(DependencyProperty property)
        : this()
    {
        _property = property;
    }

    /// <summary>Creates a trigger that is active when <paramref name="property"/> equals <paramref name="value"/>.</summary>
    /// <param name="property">The dependency property to observe.</param>
    /// <param name="value">The value that makes the trigger active.</param>
    public Trigger(DependencyProperty property, object? value)
        : this()
    {
        _property = property;
        _value = value;
    }

    /// <summary>Raised when the condition or one of the nested setters/actions changes.</summary>
    public event EventHandler? Changed;

    /// <summary>The dependency property to observe, or <see langword="null"/> while unconfigured.</summary>
    public DependencyProperty? Property
    {
        get => _property;
        set => SetField(ref _property, value);
    }

    /// <summary>The value that makes the trigger active.</summary>
    public object? Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    /// <summary>The setters applied while the trigger is active.</summary>
    public SetterCollection Setters { get; } = [];

    /// <summary>An action run when the trigger becomes active, for example a transition animation.</summary>
    public TriggerAction? EnterAction
    {
        get => _enterAction;
        set => SetField(ref _enterAction, value);
    }

    /// <summary>An action run when the trigger stops being active.</summary>
    public TriggerAction? ExitAction
    {
        get => _exitAction;
        set => SetField(ref _exitAction, value);
    }

    /// <summary>Evaluates this trigger's condition against <paramref name="control"/>.</summary>
    internal virtual bool Matches(VeloxDev.WinForms.Xaml.Controls.Control control)
        => Property is not null && Equals(control.GetValue(Property), Value);

    /// <summary>Starts observing whatever this trigger needs beyond dependency-property changes.</summary>
    internal virtual void Attach(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
    }

    /// <summary>Stops observing the control, for example when the trigger is removed.</summary>
    internal virtual void Detach(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
    }

    /// <summary>Called when the control's effective data context changed.</summary>
    internal virtual void OnDataContextChanged(VeloxDev.WinForms.Xaml.Controls.Control control)
    {
    }

    /// <summary>Raises <see cref="Changed"/>.</summary>
    protected void OnChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private void OnSettersChanged(object? sender, EventArgs e) => OnChanged();

    private void SetField<T>(ref T field, T value)
    {
        if (Equals(field, value))
        {
            return;
        }

        field = value;
        OnChanged();
    }
}

/// <summary>The triggers owned by one <see cref="VeloxDev.WinForms.Xaml.Controls.Control"/> or one <see cref="Style"/>.</summary>
public sealed class TriggerCollection : Collection<Trigger>
{
    private readonly VeloxDev.WinForms.Xaml.Controls.Control? _owner;

    /// <summary>Creates a detached collection, used by <see cref="Style.Triggers"/>.</summary>
    public TriggerCollection()
    {
    }

    internal TriggerCollection(VeloxDev.WinForms.Xaml.Controls.Control owner)
    {
        _owner = owner;
    }

    /// <summary>Raised after an item is inserted, replaced, removed or cleared.</summary>
    public event EventHandler? Changed;

    /// <inheritdoc />
    protected override void InsertItem(int index, Trigger item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        base.InsertItem(index, item);
        NotifyChanged();
    }

    /// <inheritdoc />
    protected override void SetItem(int index, Trigger item)
    {
        if (item is null) throw new ArgumentNullException(nameof(item));
        _owner?.ExitTrigger(this[index]);
        base.SetItem(index, item);
        NotifyChanged();
    }

    /// <inheritdoc />
    protected override void RemoveItem(int index)
    {
        _owner?.ExitTrigger(this[index]);
        base.RemoveItem(index);
        NotifyChanged();
    }

    /// <inheritdoc />
    protected override void ClearItems()
    {
        if (_owner is not null)
        {
            foreach (var trigger in this)
            {
                _owner.ExitTrigger(trigger);
            }
        }

        base.ClearItems();
        NotifyChanged();
    }

    private void NotifyChanged()
    {
        _owner?.RefreshTriggers();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
