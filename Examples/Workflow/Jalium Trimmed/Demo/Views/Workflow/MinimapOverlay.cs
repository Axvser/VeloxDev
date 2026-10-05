using Jalium.UI;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>Minimap overlay for the node-editor surface. Delegates data subscription and drag/click
/// navigation to the canonical <see cref="WorkflowMinimapOverlay"/>, applying this project's colours
/// for the style.</summary>
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
        => ColorConverter.ConvertFromString(hex) is Color color
            ? new SolidColorBrush(color)
            : new SolidColorBrush(Colors.Transparent);
}
