// VeloxDev customization: the minimap palette.
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// The minimap overlay: the adapter's <see cref="WorkflowMinimapOverlay"/> with this project's palette.
/// </summary>
public sealed class TemplateClass : WorkflowMinimapOverlay
{
    /// <summary>Creates the overlay and applies the generated palette.</summary>
    public TemplateClass()
    {
        MinimapBackground = ParseColor("TemplateMinimapBackground");
        MinimapBorderBrush = ParseColor("TemplateMinimapBorder");
        NodeBrush = ParseColor("TemplateNodeFill");
        ViewportStroke = ParseColor("TemplateViewportStroke");
    }
}
