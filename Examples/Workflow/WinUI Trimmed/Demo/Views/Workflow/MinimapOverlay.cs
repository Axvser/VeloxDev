using System.Globalization;
using Microsoft.UI.Xaml.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// A minimap overlay that renders a thumbnail overview of a workflow surface.
/// Delegates data subscription and drag/click navigation to the canonical
/// <see cref="WorkflowMinimapOverlay"/> adapter (same data/logic as the full demo),
/// applying the unified minimap palette for the style.
/// </summary>
public class MinimapOverlay : WorkflowMinimapOverlay
{
    public MinimapOverlay()
    {
        MinimapBackground = CreateBrush("#D2141922");
        MinimapBorderBrush = CreateBrush("#DC94A3B8");
        NodeBrush = CreateBrush("#DC38BDF8");
        ViewportStroke = CreateBrush("#F0FFFFFF");
    }

    private static Brush CreateBrush(string hex)
        => new SolidColorBrush(ParseColor(hex));

    private static Windows.UI.Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        var value = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return hex.Length == 8
            ? Windows.UI.Color.FromArgb(
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value)
            : Windows.UI.Color.FromArgb(
                0xFF,
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value);
    }
}
