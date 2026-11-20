// VeloxDev customization: the minimap palette.
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

public sealed class TemplateClass : WorkflowMinimapOverlay
{
    public TemplateClass()
    {
        MinimapBackground = ParseColor("TemplateMinimapBackground");
        MinimapBorderBrush = ParseColor("TemplateMinimapBorder");
        NodeBrush = ParseColor("TemplateNodeFill");
        ViewportStroke = ParseColor("TemplateViewportStroke");
    }
}
