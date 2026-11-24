using Jalium.UI;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>Minimap overlay for the node-editor surface. Delegates data subscription and drag/click
/// navigation to the canonical <see cref="WorkflowMinimapOverlay"/>, applying this project's colours
/// for the style.</summary>
public class MinimapOverlay : WorkflowMinimapOverlay
{
    public MinimapOverlay()
    {
        MinimapBackground = CreateBrush("#331E1E1E");
        MinimapBorderBrush = CreateBrush("#DC94A3B8");
        NodeBrush = CreateBrush("#DC38BDF8");
        ViewportStroke = CreateBrush("#F0FFFFFF");
    }

    /// <summary>Customize the minimap drawing here; keep the base call unless you replace it entirely.</summary>
    protected override void DrawMinimap(DrawingContext dc, MinimapDrawContext context)
    {
        base.DrawMinimap(dc, context);
    }

    private static Brush CreateBrush(string hex)
        => ColorConverter.ConvertFromString(hex) is Color color
            ? new SolidColorBrush(color)
            : new SolidColorBrush(Colors.Transparent);
}
