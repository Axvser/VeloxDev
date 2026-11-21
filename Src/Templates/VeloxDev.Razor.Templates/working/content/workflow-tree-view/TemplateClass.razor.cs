// VeloxDev customization: Provide an ITreeComponent as the component parameter; tree-specific behavior lives here.
using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;

namespace TemplateNamespace;

/// <summary>A Razor/Blazor workflow tree surface over the shared surface behavior.</summary>
public partial class TemplateClass : ComponentBase
{
    /// <summary>Gets or sets the workflow tree rendered by this surface.</summary>
    [Parameter]
    public ITreeComponent? Tree { get; set; }

    /// <summary>Gets or sets the scroll container element id.</summary>
    [Parameter]
    public string ScrollViewerId { get; set; } = "veloxdev-wf-scroll";

    /// <summary>Gets or sets the canvas element id.</summary>
    [Parameter]
    public string CanvasId { get; set; } = "veloxdev-wf-canvas";

    /// <summary>Gets or sets an optional per-node template (overrides the generated <c>NodeView</c>).</summary>
    [Parameter]
    public RenderFragment<INodeComponent>? NodeTemplate { get; set; }

    /// <summary>Gets or sets the minor grid spacing in pixels.</summary>
    [Parameter]
    public double GridSpacing { get; set; } = 40;
}
