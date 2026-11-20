// VeloxDev customization: the minimap palette.
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The minimap overlay: the adapter's <see cref="WorkflowMinimapOverlay"/> with this project's palette. Name it
/// PART_MinimapOverlay in the surface markup and the surface behavior pushes the offsets and tree into it.
/// </summary>
public sealed class MinimapOverlay : WorkflowMinimapOverlay
{
    /// <summary>Creates the overlay and applies the palette.</summary>
    public MinimapOverlay()
    {
        MinimapBackground = ParseColor("#D21F1F1F");
        MinimapBorderBrush = ParseColor("#DC404040");
        NodeBrush = ParseColor("#DC6E8BFF");
        ViewportStroke = ParseColor("#F0E7E7E7");
    }
}
