// VeloxDev customization: the surface owns pan, zoom, link drawing and the input route; set ViewModel from the host.
using System.ComponentModel;
using System.Globalization;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.VisualHosting;

namespace TemplateNamespace;

/// <summary>A workflow surface on the visual backend, with this project's palette.</summary>
public sealed partial class TemplateClass : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    /// <summary>Creates the surface and applies the palette.</summary>
    public TemplateClass()
    {
        InitializeComponent();

        var background = XamlRuntime.ParseColor("TemplateSurfaceBackground");
        workflowHost.BackColor = background;
        workflowHost.GridDecorator.Background = background;
        workflowHost.GridDecorator.BorderBrush = XamlRuntime.ParseColor("TemplateSurfaceBorderBrush");
        workflowHost.GridDecorator.BorderThickness = XamlRuntime.ParseThickness("TemplateSurfaceBorderThickness");
        workflowHost.GridDecorator.CornerRadius = float.Parse(
            "TemplateSurfaceCornerRadius",
            CultureInfo.InvariantCulture);
        workflowHost.LinkLayer.LineColor = XamlRuntime.ParseColor("#DDFFFFFF");
    }

    /// <summary>The tree rendered by this surface, or <see langword="null"/>.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public ITreeComponent? ViewModel
    {
        get => workflowHost.Tree;
        set => workflowHost.Tree = value;
    }

    /// <summary>Chooses the node card before the surface's default card is used.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public VisualTemplateSelector? TemplateSelector
    {
        get => workflowHost.TemplateSelector;
        set => workflowHost.TemplateSelector = value;
    }

    /// <summary>The visual surface this control hosts.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public VisualWorkflowSurface Surface => workflowHost;
}
