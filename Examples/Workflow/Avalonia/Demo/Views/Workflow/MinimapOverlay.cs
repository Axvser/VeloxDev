using Avalonia.Media;
using Avalonia.Media.Immutable;
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
        MinimapBackground = CreateBrush("#D2141922");
        MinimapBorderBrush = CreateBrush("#DC94A3B8");
        NodeBrush = CreateBrush("#DC38BDF8");
        ViewportStroke = CreateBrush("#F0FFFFFF");
    }

    private static IBrush CreateBrush(string hex)
        => new ImmutableSolidColorBrush(Color.Parse(hex));
}
