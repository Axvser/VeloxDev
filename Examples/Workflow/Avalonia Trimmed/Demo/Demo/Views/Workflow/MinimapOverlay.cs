using Avalonia.Media;
using Avalonia.Media.Immutable;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo;

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
        MinimapBackground = CreateBrush("#331E1E1E");
        MinimapBorderBrush = CreateBrush("#DC94A3B8");
        NodeBrush = CreateBrush("#DC38BDF8");
        ViewportStroke = CreateBrush("#F0FFFFFF");
    }

    /// <summary>Customize the minimap drawing here; keep the base call unless you replace it entirely.</summary>
    protected override void DrawMinimap(DrawingContext drawingContext, MinimapDrawContext context)
    {
        base.DrawMinimap(drawingContext, context);
    }

    private static IBrush CreateBrush(string hex)
        => new ImmutableSolidColorBrush(Color.Parse(hex));
}
