// VeloxDev customization: the minimap palette.
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem.VisualHosting;

namespace TemplateNamespace;

/// <summary>The workflow minimap overlay with this project's palette.</summary>
public sealed class TemplateClass : VisualWorkflowMinimap
{
    /// <summary>Creates the minimap and applies the palette.</summary>
    public TemplateClass()
    {
        Background = XamlRuntime.ParseColor("TemplateMinimapBackground");
        BorderBrush = XamlRuntime.ParseColor("TemplateMinimapBorder");
        NodeBrush = XamlRuntime.ParseColor("TemplateNodeFill");
        ViewportStroke = XamlRuntime.ParseColor("TemplateViewportStroke");
    }
}
