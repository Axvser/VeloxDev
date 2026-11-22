using System.Windows.Forms;

namespace VeloxDev.WinForms.Xaml;

/// <summary>Receives the old and new values after a dependency property has been changed.</summary>
/// <param name="target">The <see cref="System.Windows.Forms.Control"/> whose property changed.</param>
/// <param name="e">The <see cref="DependencyPropertyChangedEventArgs"/> carrying both values.</param>
public delegate void PropertyChangedCallback(System.Windows.Forms.Control target, DependencyPropertyChangedEventArgs e);

/// <summary>Coerces a value before it is stored in a dependency property.</summary>
/// <param name="target">The <see cref="System.Windows.Forms.Control"/> that is receiving the value.</param>
/// <param name="baseValue">The value supplied by the caller.</param>
/// <returns>The value that is actually stored.</returns>
public delegate object? CoerceValueCallback(System.Windows.Forms.Control target, object? baseValue);

/// <summary>The old and new values of one dependency property change.</summary>
public readonly struct DependencyPropertyChangedEventArgs
{
    /// <summary>Creates the event arguments for one dependency property change.</summary>
    /// <param name="property">The <see cref="DependencyProperty"/> that changed.</param>
    /// <param name="oldValue">The value before the change.</param>
    /// <param name="newValue">The value after the change.</param>
    public DependencyPropertyChangedEventArgs(DependencyProperty property, object? oldValue, object? newValue)
        : this(property, oldValue, newValue, BindingPriority.LocalValue)
    {
    }

    /// <summary>Creates the event arguments for one dependency property change at a priority layer.</summary>
    /// <param name="property">The <see cref="DependencyProperty"/> that changed.</param>
    /// <param name="oldValue">The value before the change.</param>
    /// <param name="newValue">The value after the change.</param>
    /// <param name="priority">The priority layer that changed.</param>
    public DependencyPropertyChangedEventArgs(
        DependencyProperty property,
        object? oldValue,
        object? newValue,
        BindingPriority priority)
    {
        Property = property;
        OldValue = oldValue;
        NewValue = newValue;
        Priority = priority;
    }

    /// <summary>The <see cref="DependencyProperty"/> that changed.</summary>
    public DependencyProperty Property { get; }

    /// <summary>The value before the change.</summary>
    public object? OldValue { get; }

    /// <summary>The value after the change.</summary>
    public object? NewValue { get; }

    /// <summary>The priority layer that changed.</summary>
    public BindingPriority Priority { get; }
}

/// <summary>The default value, change notification and coercion hooks of a dependency property.</summary>
/// <remarks>
/// Deliberately smaller than WPF's metadata table: this layer supports a single default value and two callbacks.
/// Inheritance, value precedence and per-type overrides are not part of the subset.
/// </remarks>
public class PropertyMetadata
{
    /// <summary>Creates metadata with only a default value.</summary>
    /// <param name="defaultValue">The value read when no local value is set.</param>
    public PropertyMetadata(object? defaultValue)
        : this(defaultValue, null, null)
    {
    }

    /// <summary>Creates metadata with a default value and a change callback.</summary>
    /// <param name="defaultValue">The value read when no local value is set.</param>
    /// <param name="propertyChangedCallback">Invoked after a value is stored.</param>
    public PropertyMetadata(object? defaultValue, PropertyChangedCallback? propertyChangedCallback)
        : this(defaultValue, propertyChangedCallback, null)
    {
    }

    /// <summary>Creates metadata with a default value, a change callback and a coercion callback.</summary>
    /// <param name="defaultValue">The value read when no local value is set.</param>
    /// <param name="propertyChangedCallback">Invoked after a value is stored.</param>
    /// <param name="coerceValueCallback">Invoked before a value is stored.</param>
    public PropertyMetadata(
        object? defaultValue,
        PropertyChangedCallback? propertyChangedCallback,
        CoerceValueCallback? coerceValueCallback)
    {
        DefaultValue = defaultValue;
        PropertyChangedCallback = propertyChangedCallback;
        CoerceValueCallback = coerceValueCallback;
    }

    /// <summary>The value read when no local value is set.</summary>
    public object? DefaultValue { get; }

    /// <summary>The callback invoked after a value is stored, or <see langword="null"/>.</summary>
    public PropertyChangedCallback? PropertyChangedCallback { get; }

    /// <summary>The callback invoked before a value is stored, or <see langword="null"/>.</summary>
    public CoerceValueCallback? CoerceValueCallback { get; }

    /// <summary>
    /// Whether a change to this property invalidates the target XAML control.
    /// </summary>
    /// <remarks>
    /// The default matches the visual-property case: a control that paints from a dependency property is repainted
    /// when the value changes, without a per-property callback. Set it to <see langword="false"/> for configuration
    /// properties that never reach a paint pass. Native WinForms targets are never invalidated by this flag.
    /// </remarks>
    public bool AffectsRender { get; init; } = true;

    /// <summary>
    /// Whether a change to this property invalidates the measure of the target and its ancestors.
    /// </summary>
    /// <remarks>
    /// A measure invalidation also runs the layout pass that arranges the affected subtree. Set it for properties that
    /// change a desired size, such as <c>Width</c>, <c>Padding</c>, text or a panel's spacing.
    /// </remarks>
    public bool AffectsMeasure { get; init; }

    /// <summary>
    /// Whether a change to this property invalidates the arrange of the target's subtree without changing its measure.
    /// </summary>
    /// <remarks>
    /// Use it for alignment or position properties that do not change the desired size. <see cref="AffectsMeasure"/>
    /// already implies an arrange pass; a property never needs both flags.
    /// </remarks>
    public bool AffectsArrange { get; init; }

    /// <summary>
    /// Whether an ancestor's value is used when the target has no value of its own.
    /// </summary>
    /// <remarks>Used by the visual backend's <c>DataContext</c>; the HWND backend leaves it false.</remarks>
    public bool Inherits { get; init; }
}
