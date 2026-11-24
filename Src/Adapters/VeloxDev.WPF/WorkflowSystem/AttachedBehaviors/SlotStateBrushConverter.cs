using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Maps a slot's runtime <see cref="SlotState"/> to the port glyph brush.</summary>
/// <remarks>
/// A slot template binds the glyph's <c>Fill</c> to its data context's <c>State</c> through this converter, so the
/// view code-behind stays at <c>InitializeComponent()</c>. Pass the <c>Border</c> converter parameter to receive
/// <see cref="BorderColor"/> instead, which lets one instance paint the outline.
/// </remarks>
public sealed class SlotStateBrushConverter : IValueConverter
{
    /// <summary>The colour of a slot in <see cref="SlotState.StandBy"/>.</summary>
    public Color StandByColor { get; set; } = Color.FromRgb(0x1E, 0x1E, 0x1E);

    /// <summary>The colour returned when the binding passes the <c>Border</c> converter parameter.</summary>
    public Color BorderColor { get; set; } = Colors.White;

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new SolidColorBrush(IsBorder(parameter) ? BorderColor : ResolveStateColor(value));

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private Color ResolveStateColor(object? value)
    {
        var state = value is SlotState slotState ? slotState : SlotState.StandBy;
        return state switch
        {
            var s when s.HasFlag(SlotState.Sender) && s.HasFlag(SlotState.Receiver) => Colors.Violet,
            var s when s.HasFlag(SlotState.Sender) || s.HasFlag(SlotState.PreviewSender) => Colors.Tomato,
            var s when s.HasFlag(SlotState.Receiver) || s.HasFlag(SlotState.PreviewReceiver) => Colors.Lime,
            _ => StandByColor,
        };
    }

    private static bool IsBorder(object? parameter)
        => parameter is string text && string.Equals(text, "Border", StringComparison.OrdinalIgnoreCase);
}
