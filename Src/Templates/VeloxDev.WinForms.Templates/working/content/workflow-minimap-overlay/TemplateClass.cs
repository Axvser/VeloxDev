// VeloxDev customization: the minimap palette.
using System.Drawing;
using VeloxDev.WorkflowSystem;
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

    /// <summary>Customize the minimap drawing here; keep the base call unless you replace it entirely.</summary>
    protected override void DrawMinimap(Graphics graphics, MinimapDrawContext context)
    {
        base.DrawMinimap(graphics, context);
    }
}
