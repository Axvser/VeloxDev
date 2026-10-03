// VeloxDev customization: The minimap overlay. The adapter's WorkflowMinimapOverlay already owns the layout
// maths, the drag mapping and the top-right anchoring; this file supplies the palette only. Assign it to the
// tree view's MinimapOverlay property to wire it up.
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// The minimap overlay: the adapter's <see cref="WorkflowMinimapOverlay"/> with this project's palette.
/// </summary>
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
