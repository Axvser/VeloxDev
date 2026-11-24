using Microsoft.Maui.Graphics;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

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
        MinimapBackgroundColor = Color.FromArgb("#D2141922");
        MinimapBorderColor = Color.FromArgb("#DC94A3B8");
        NodeFillColor = Color.FromArgb("#DC38BDF8");
        ViewportStrokeColor = Color.FromArgb("#F0FFFFFF");
    }

    /// <summary>Customize the minimap drawing here; keep the base call unless you replace it entirely.</summary>
    protected override void DrawMinimap(ICanvas canvas, MinimapDrawContext context)
    {
        base.DrawMinimap(canvas, context);
    }
}
