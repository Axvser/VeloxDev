using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using XamlControl = VeloxDev.WinForms.Xaml.Controls.Control;

namespace VeloxDev.WorkflowSystem.Attachments;

/// <summary>Reads and writes a card colour through the XAML brush properties when the target is a XAML control.</summary>
internal static class XamlControlColors
{
    internal static Color GetBackground(System.Windows.Forms.Control target)
        => target is XamlControl xaml ? xaml.EffectiveBackground : target.BackColor;

    internal static Color GetForeground(System.Windows.Forms.Control target)
        => target is XamlControl xaml ? xaml.EffectiveForeground : target.ForeColor;

    internal static void SetBackground(System.Windows.Forms.Control target, Color color)
    {
        if (target is XamlControl xaml)
        {
            xaml.Background = new SolidColorBrush(color);
            return;
        }

        target.BackColor = color;
    }
}
