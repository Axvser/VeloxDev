using System.Windows.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>
/// A minimap overlay that renders a thumbnail overview of a workflow surface.
/// Delegates data subscription and drag/click navigation to the canonical
/// <see cref="WorkflowMinimapOverlay"/> adapter (same data/logic as the full demo),
/// applying the template color symbols for the style.
/// </summary>
public class TemplateClass : WorkflowMinimapOverlay
{
    public TemplateClass()
    {
        MinimapBackground = CreateBrush("TemplateMinimapBackground");
        MinimapBorderBrush = CreateBrush("TemplateMinimapBorder");
        NodeBrush = CreateBrush("TemplateNodeFill");
        ViewportStroke = CreateBrush("TemplateViewportStroke");
    }

    private static Brush CreateBrush(string hex)
        => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
}
