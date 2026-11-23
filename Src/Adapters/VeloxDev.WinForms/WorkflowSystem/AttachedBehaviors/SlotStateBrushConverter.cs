using System.Drawing;
using VeloxDev.WinForms.Xaml;
using XamlBrush = VeloxDev.WinForms.Xaml.Brush;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Maps a slot's runtime <see cref="SlotState"/> to the port glyph colour.</summary>
/// <remarks>
/// The slot template binds <c>PART_Glyph.Fill</c> to its data context's <c>State</c> through this converter; the
/// view code-behind stays empty while the four state colours remain model-driven.
/// </remarks>
public sealed class SlotStateBrushConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType)
    {
        var state = value is SlotState slotState
            ? slotState
            : SlotState.StandBy;

        var color = state switch
        {
            var s when s.HasFlag(SlotState.Sender)
                       && s.HasFlag(SlotState.Receiver) => Color.Violet,
            var s when s.HasFlag(SlotState.Sender)
                       || s.HasFlag(SlotState.PreviewSender) => Color.Tomato,
            var s when s.HasFlag(SlotState.Receiver)
                       || s.HasFlag(SlotState.PreviewReceiver) => Color.Lime,
            _ => Color.FromArgb(0xDD, 0x1E, 0x1E, 0x1E),
        };

        return typeof(XamlBrush).IsAssignableFrom(targetType) ? new SolidColorBrush(color) : color;
    }

    /// <inheritdoc />
    public object? ConvertBack(object? value, Type sourceType)
        => throw new NotSupportedException();
}
