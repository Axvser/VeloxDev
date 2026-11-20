namespace VeloxDev.WinForms.Xaml;

/// <summary>How a <see cref="Binding"/> moves values between a source object and the target.</summary>
public enum BindingMode
{
    /// <summary>Source to target, and again whenever the source raises a change notification.</summary>
    OneWay,

    /// <summary>Source to target and target back to source.</summary>
    TwoWay,

    /// <summary>Source to target once, when the binding is first attached.</summary>
    OneTime,
}

/// <summary>Converts a bound value on its way to the target and, for two-way bindings, back to the source.</summary>
public interface IValueConverter
{
    /// <summary>Converts a source value for the target property.</summary>
    /// <param name="value">The value read from the source.</param>
    /// <param name="targetType">The type of the property receiving the value.</param>
    object? Convert(object? value, Type targetType);

    /// <summary>Converts a target value back for the source property.</summary>
    /// <param name="value">The value read from the target.</param>
    /// <param name="sourceType">The type of the property receiving the value.</param>
    object? ConvertBack(object? value, Type sourceType);
}

/// <summary>
/// Describes a one-way, two-way or one-time link from a property on the effective data context to one dependency
/// property, or to one native WinForms property through <c>Control.DataBindings</c>.
/// </summary>
public sealed class Binding
{
    /// <summary>Creates an unconfigured binding.</summary>
    public Binding()
    {
    }

    /// <summary>Creates a binding with <paramref name="path"/>.</summary>
    /// <param name="path">A dot-separated property path relative to the data context; empty means the context itself.</param>
    public Binding(string path)
    {
        Path = path;
    }

    /// <summary>A dot-separated property path relative to the data context; empty means the context itself.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>How values move between source and target.</summary>
    public BindingMode Mode { get; set; } = BindingMode.OneWay;

    /// <summary>An optional converter applied in both directions.</summary>
    public IValueConverter? Converter { get; set; }
}
