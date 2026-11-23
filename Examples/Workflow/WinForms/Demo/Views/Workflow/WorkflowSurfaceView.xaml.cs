// VeloxDev customization: the visual tree of the workflow surface. WorkflowCanvas hosts it in a VisualHost.

namespace Demo.Views.Workflow;

/// <summary>The named visual parts of the workflow surface.</summary>
public sealed partial class WorkflowSurfaceView : VeloxDev.WinForms.Xaml.Visuals.Controls.UserControl
{
    /// <summary>Creates the surface tree from XAML.</summary>
    public WorkflowSurfaceView()
    {
        InitializeComponent();
    }

    /// <summary>The canvas part that hosts pooled node and link views.</summary>
    public VeloxDev.WinForms.Xaml.Visuals.Controls.Canvas Canvas => PART_Canvas;

    /// <summary>The scroll viewer part.</summary>
    public VeloxDev.WinForms.Xaml.Visuals.Controls.ScrollViewer ScrollViewer => PART_ScrollViewer;

    /// <summary>The grid and ruler decorator part.</summary>
    public GridDecorator GridDecorator => PART_GridDecorator;

    /// <summary>The minimap visual part.</summary>
    public MinimapOverlay MinimapOverlay => PART_MinimapOverlay;

    /// <summary>The info HUD visual part.</summary>
    public VeloxDev.WorkflowSystem.AttachedBehaviors.WorkflowInfoOverlay InfoOverlay => PART_InfoOverlay;
}
