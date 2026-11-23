using System.Drawing;
using VeloxDev.WinForms.Xaml;
using XamlBrush = VeloxDev.WinForms.Xaml.Brush;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Maps a slot's runtime <see cref="SlotState"/> to the port glyph colour.</summary>
/// <remarks>
/// <para>
/// The slot template binds <c>PART_Glyph.Fill</c> to its data context's <c>State</c> through this converter; the
/// view code-behind stays empty while the four state colours remain model-driven.
/// </para>
/// <para>
/// <see cref="StandByColor"/> and <see cref="BorderColor"/> let a template declare its own palette. A binding that
/// passes the <c>Border</c> converter parameter receives <see cref="BorderColor"/> instead of the state colour, so
/// the same instance can paint the glyph's outline.
/// </para>
/// </remarks>
public sealed class SlotStateBrushConverter : IValueConverter, IParameterizedValueConverter
{
    /// <summary>The colour of a slot in <see cref="SlotState.StandBy"/>.</summary>
    public Color StandByColor { get; set; } = Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E);

    /// <summary>The colour returned when the binding passes the <c>Border</c> converter parameter.</summary>
    public Color BorderColor { get; set; } = Color.White;

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType) => Convert(value, targetType, null);

    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter)
    {
        var color = IsBorder(parameter) ? BorderColor : ResolveStateColor(value);
        return typeof(XamlBrush).IsAssignableFrom(targetType) ? new SolidColorBrush(color) : color;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type sourceType)
        => throw new NotSupportedException();

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type sourceType, object? parameter)
        => throw new NotSupportedException();

    private Color ResolveStateColor(object? value)
    {
        var state = value is SlotState slotState ? slotState : SlotState.StandBy;
        return state switch
        {
            var s when s.HasFlag(SlotState.Sender)
                       && s.HasFlag(SlotState.Receiver) => Color.Violet,
            var s when s.HasFlag(SlotState.Sender)
                       || s.HasFlag(SlotState.PreviewSender) => Color.Tomato,
            var s when s.HasFlag(SlotState.Receiver)
                       || s.HasFlag(SlotState.PreviewReceiver) => Color.Lime,
            _ => StandByColor,
        };
    }

    private static bool IsBorder(object? parameter)
        => parameter is string text && string.Equals(text, "Border", StringComparison.OrdinalIgnoreCase);
}
