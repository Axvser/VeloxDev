using Microsoft.Maui.Graphics;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

/// <summary>
/// A minimap overlay that renders a thumbnail overview of a workflow surface. It delegates data
/// subscription and drag/click navigation to the canonical <see cref="WorkflowMinimapOverlay"/>
/// adapter (same data/logic as the full demo), and adds only the unified palette — the base paints
/// nothing without it.
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
}
