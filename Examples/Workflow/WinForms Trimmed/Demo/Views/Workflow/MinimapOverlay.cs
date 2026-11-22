// VeloxDev customization: the minimap palette.
using VeloxDev.WinForms.Xaml;
using VeloxDev.WorkflowSystem.VisualHosting;

namespace Demo.Views.Workflow;

/// <summary>The workflow minimap overlay with this project's palette.</summary>
public sealed class MinimapOverlay : VisualWorkflowMinimap
{
    /// <summary>Creates the minimap and applies the palette.</summary>
    public MinimapOverlay()
    {
        Background = XamlRuntime.ParseColor("#D2141922");
        BorderBrush = XamlRuntime.ParseColor("#DC94A3B8");
        NodeBrush = XamlRuntime.ParseColor("#DC38BDF8");
        ViewportStroke = XamlRuntime.ParseColor("#F0FFFFFF");
    }
}
