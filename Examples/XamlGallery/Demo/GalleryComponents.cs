using System.Globalization;
using VeloxDev.TransitionSystem;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Controls;
using XamlControl = VeloxDev.WinForms.Xaml.Controls.Control;

namespace Demo;

/// <summary>A small control whose properties are generated as dependency properties.</summary>
/// <remarks>The styles section templates it with a <see cref="ControlTemplate"/> and drives it with triggers.</remarks>
public sealed partial class GalleryBadge : XamlControl
{
    /// <summary>The text shown inside the badge.</summary>
    [XamlProperty(DefaultValue = "Badge")]
    public partial string Label { get; set; }

    /// <summary>The first template-trigger condition.</summary>
    [XamlProperty]
    public partial bool IsHot { get; set; }

    /// <summary>The second template-trigger condition.</summary>
    [XamlProperty]
    public partial bool IsPinned { get; set; }

    /// <summary>A value written by a data or multi trigger in the styles section.</summary>
    [XamlProperty(DefaultValue = "")]
    public partial string Note { get; set; }
}

/// <summary>Chooses the enabled or muted row template for the data section.</summary>
public sealed class EnabledRowTemplateSelector : TemplateSelector
{
    /// <summary>The template used for enabled rows.</summary>
    public DataTemplate? EnabledTemplate { get; set; }

    /// <summary>The template used for disabled rows.</summary>
    public DataTemplate? DisabledTemplate { get; set; }

    /// <inheritdoc />
    public override DataTemplate? SelectTemplate(object? item)
        => item is SampleRow { Enabled: false } ? DisabledTemplate : EnabledTemplate;
}

/// <summary>Prefixes a bound value with the converter parameter.</summary>
public sealed class PrefixConverter : IValueConverter, IParameterizedValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType) => value;

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type sourceType) => value;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter)
        => $"{parameter}{value}";

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type sourceType, object? parameter) => value;
}

/// <summary>A custom trigger action that rounds a border's corners when the trigger enters.</summary>
public sealed class PulseAction : TriggerAction
{
    /// <summary>The corner radius reached by the pulse.</summary>
    public float Radius { get; set; } = 24f;

    /// <inheritdoc />
    public override void Invoke(XamlControl control)
    {
        if (control is not Border border || border.IsDisposed)
        {
            return;
        }

        XamlTransition.Animate(
            border,
            Border.CornerRadiusProperty,
            Radius,
            new TransitionEffect
            {
                Duration = TimeSpan.FromMilliseconds(180),
                Ease = Eases.Cubic.Out,
            });
    }
}

/// <summary>A validation rule that rejects values longer than a fixed length.</summary>
public sealed class MaxLengthRule : ValidationRule
{
    /// <summary>The largest accepted length.</summary>
    public int Maximum { get; set; } = 12;

    /// <inheritdoc />
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        var text = value?.ToString() ?? string.Empty;
        return text.Length <= Maximum
            ? ValidationResult.ValidResult
            : new ValidationResult(false, $"Keep this under {Maximum} characters.");
    }
}
