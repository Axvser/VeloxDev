using System.Globalization;
using Microsoft.UI.Xaml.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// A minimap overlay that renders a thumbnail overview of a workflow surface.
/// Delegates data subscription and drag/click navigation to the canonical
/// <see cref="WorkflowMinimapOverlay"/> adapter (same data/logic as the full demo),
/// applying the template color symbols for the style.
/// </summary>
public class TemplateClass : WorkflowMinimapOverlay
{
    public TemplateClass()
    {
        MinimapBackground = CreateBrush("TemplateMinimapBackground");
        MinimapBorderBrush = CreateBrush("TemplateMinimapBorder");
        NodeBrush = CreateBrush("TemplateNodeFill");
        ViewportStroke = CreateBrush("TemplateViewportStroke");
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
