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
    {
        Property = property;
        OldValue = oldValue;
        NewValue = newValue;
    }

    /// <summary>The <see cref="DependencyProperty"/> that changed.</summary>
    public DependencyProperty Property { get; }

    /// <summary>The value before the change.</summary>
    public object? OldValue { get; }

    /// <summary>The value after the change.</summary>
    public object? NewValue { get; }
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
}
