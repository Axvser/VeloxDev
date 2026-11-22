// VeloxDev customization: the minimap palette; the adapter's minimap is composed, not inherited.
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem.VisualHosting;
using VisualBorder = VeloxDev.WinForms.Xaml.Visuals.Controls.Border;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;

namespace Demo.Views.Workflow;

/// <summary>The workflow minimap overlay with this project's palette.</summary>
public sealed class MinimapOverlay : VisualBorder
{
    /// <summary>Creates the minimap and applies the palette.</summary>
    public MinimapOverlay()
    {
        Padding = new XamlThickness(0f);
        Child = new VisualWorkflowMinimap
        {
            Background = XamlRuntime.ParseColor("#D2141922"),
            BorderBrush = XamlRuntime.ParseColor("#DC94A3B8"),
            NodeBrush = XamlRuntime.ParseColor("#DC38BDF8"),
            ViewportStroke = XamlRuntime.ParseColor("#F0FFFFFF"),
        };
    }
}
