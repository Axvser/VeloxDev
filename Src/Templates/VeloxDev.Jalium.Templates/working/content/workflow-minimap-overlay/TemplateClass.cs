using Jalium.UI;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

/// <summary>Minimap overlay for the node-editor surface. Delegates data subscription and drag/click
/// navigation to the canonical <see cref="WorkflowMinimapOverlay"/>, applying the template colour symbols
/// for the style.</summary>
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
        => ColorConverter.ConvertFromString(hex) is Color color
            ? new SolidColorBrush(color)
            : new SolidColorBrush(Colors.Transparent);
}
