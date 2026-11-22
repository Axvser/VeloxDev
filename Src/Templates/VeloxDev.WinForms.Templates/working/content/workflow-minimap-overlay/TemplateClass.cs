// VeloxDev customization: the minimap palette; the adapter's minimap is composed, not inherited.
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem.VisualHosting;
using VisualBorder = VeloxDev.WinForms.Xaml.Visuals.Controls.Border;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace TemplateNamespace;

/// <summary>The workflow minimap overlay with this project's palette.</summary>
public sealed class TemplateClass : VisualBorder
{
    /// <summary>Creates the minimap and applies the palette.</summary>
    public TemplateClass()
    {
        Padding = new XamlThickness(0f);
        Child = new VisualWorkflowMinimap
        {
            Background = XamlRuntime.ParseColor("TemplateMinimapBackground"),
            BorderBrush = XamlRuntime.ParseColor("TemplateMinimapBorder"),
            NodeBrush = XamlRuntime.ParseColor("TemplateNodeFill"),
            ViewportStroke = XamlRuntime.ParseColor("TemplateViewportStroke"),
        };
    }
}
