using System.Globalization;

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

/// <summary>When a two-way binding writes the target value back to its source.</summary>
public enum UpdateSourceTrigger
{
    /// <summary>Use the binding engine's default, which is <see cref="PropertyChanged"/> for dependency properties.</summary>
    Default,

    /// <summary>Write back after every target value change.</summary>
    PropertyChanged,

    /// <summary>Write back when the target loses focus.</summary>
    LostFocus,

    /// <summary>Write back when the host edits the value.</summary>
    Explicit,
}

/// <summary>Describes where a <see cref="Binding"/> finds its source object.</summary>
public enum RelativeSourceMode
{
    /// <summary>Use the control that owns the binding.</summary>
    Self,

    /// <summary>Use the templated parent of the control that owns the binding.</summary>
    TemplatedParent,

    /// <summary>Use the nearest ancestor of <see cref="RelativeSource.AncestorType"/>.</summary>
    FindAncestor,
}

/// <summary>An element-relative source for a <see cref="Binding"/>.</summary>
public sealed class RelativeSource
{
    /// <summary>Creates an unconfigured relative source.</summary>
    public RelativeSource()
    {
    }

    /// <summary>Creates a relative source in <paramref name="mode"/>.</summary>
    /// <param name="mode">The lookup mode.</param>
    public RelativeSource(RelativeSourceMode mode)
    {
        Mode = mode;
    }

    /// <summary>The lookup mode.</summary>
    public RelativeSourceMode Mode { get; set; }

    /// <summary>The ancestor type used by <see cref="RelativeSourceMode.FindAncestor"/>.</summary>
    public Type? AncestorType { get; set; }

    /// <summary>The one-based ancestor depth; one means the nearest matching ancestor.</summary>
    public int AncestorLevel { get; set; } = 1;

    /// <summary>A source on the binding's own control.</summary>
    public static RelativeSource Self => new(RelativeSourceMode.Self);

    /// <summary>A source on the control that owns the applied control template.</summary>
    public static RelativeSource TemplatedParent => new(RelativeSourceMode.TemplatedParent);
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
/// An optional converter contract that receives the binding's <see cref="Binding.ConverterParameter"/>.
/// </summary>
/// <remarks>
/// <see cref="IValueConverter"/> predates parameters and cannot grow an overload without breaking implementers on
/// the older target frameworks. A converter that needs the parameter implements this interface in addition.
/// </remarks>
public interface IParameterizedValueConverter
{
    /// <summary>Converts a source value for the target property.</summary>
    /// <param name="value">The value read from the source.</param>
    /// <param name="targetType">The type of the property receiving the value.</param>
    /// <param name="parameter">The binding's converter parameter.</param>
    object? Convert(object? value, Type targetType, object? parameter);

    /// <summary>Converts a target value back for the source property.</summary>
    /// <param name="value">The value read from the target.</param>
    /// <param name="sourceType">The type of the property receiving the value.</param>
    /// <param name="parameter">The binding's converter parameter.</param>
    object? ConvertBack(object? value, Type sourceType, object? parameter);
}

/// <summary>
/// Describes a one-way, two-way or one-time link from a source object to one dependency property, or to one native
/// WinForms property through a binding sink.
/// </summary>
/// <remarks>
/// <para>
/// The source is, in order of precedence, <see cref="Source"/>, the element named by <see cref="ElementName"/>,
/// <see cref="RelativeSource"/>, or the effective data context. A binding with none of those set behaves like the
/// original data-context binding.
/// </para>
/// <para>
/// <see cref="StringFormat"/> and <see cref="FallbackValue"/> are applied after the converter, which matches WPF's
/// order. <see cref="TargetNullValue"/> replaces a source value that is <see langword="null"/>.
/// </para>
/// </remarks>
public class Binding
{
    /// <summary>Creates an unconfigured binding.</summary>
    public Binding()
    {
    }

    /// <summary>Creates a binding with <paramref name="path"/>.</summary>
    /// <param name="path">A dot-separated property path relative to the source; empty means the source itself.</param>
    public Binding(string path)
    {
        Path = path;
    }

    /// <summary>A dot-separated property path relative to the source; empty means the source itself.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>How values move between source and target.</summary>
    public BindingMode Mode { get; set; } = BindingMode.OneWay;

    /// <summary>An optional explicit source object; overrides <see cref="ElementName"/> and <see cref="RelativeSource"/>.</summary>
    public object? Source { get; set; }

    /// <summary>The name of a source element in the nearest <see cref="NameScope"/>.</summary>
    public string? ElementName { get; set; }

    /// <summary>An element-relative source such as <c>Self</c>, <c>TemplatedParent</c> or <c>FindAncestor</c>.</summary>
    public RelativeSource? RelativeSource { get; set; }

    /// <summary>An optional converter applied in both directions.</summary>
    public IValueConverter? Converter { get; set; }

    /// <summary>A value passed to <see cref="IParameterizedValueConverter"/> alongside the bound value.</summary>
    public object? ConverterParameter { get; set; }

    /// <summary>A composite format applied after conversion; a bare format such as <c>N2</c> is wrapped as <c>{0:N2}</c>.</summary>
    public string? StringFormat { get; set; }

    /// <summary>The value used when the path cannot be resolved or the source is missing.</summary>
    public object? FallbackValue { get; set; }

    /// <summary>The value used when the resolved source value is <see langword="null"/>.</summary>
    public object? TargetNullValue { get; set; }

    /// <summary>When a two-way binding writes the target value back.</summary>
    public UpdateSourceTrigger UpdateSourceTrigger { get; set; }

    /// <summary>Whether an exception thrown while converting or writing back becomes a validation error.</summary>
    public bool ValidatesOnExceptions { get; set; }

    /// <summary>Whether binding errors are surfaced as events.</summary>
    public bool NotifyOnValidationError { get; set; }

    /// <summary>Whether a source implementing <see cref="System.ComponentModel.INotifyDataErrorInfo"/> contributes its errors.</summary>
    public bool ValidatesOnNotifyDataErrors { get; set; } = true;

    /// <summary>Whether a source implementing <see cref="System.ComponentModel.IDataErrorInfo"/> contributes its errors.</summary>
    public bool ValidatesOnDataErrors { get; set; } = true;
}

/// <summary>
/// A binding whose source is the templated parent.
/// </summary>
public sealed class TemplateBinding : Binding
{
    /// <summary>Creates a one-way template binding over <paramref name="path"/>.</summary>
    /// <param name="path">The templated parent's property path.</param>
    public TemplateBinding(string path)
        : base(path)
    {
        RelativeSource = Xaml.RelativeSource.TemplatedParent;
    }

    /// <summary>Creates a template binding over <paramref name="path"/> in <paramref name="mode"/>.</summary>
    /// <param name="path">The templated parent's property path.</param>
    /// <param name="mode">The binding mode.</param>
    public TemplateBinding(string path, BindingMode mode)
        : this(path)
    {
        Mode = mode;
    }
}
