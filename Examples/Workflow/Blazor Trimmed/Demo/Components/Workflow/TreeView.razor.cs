using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

/// <summary>
/// A Razor/Blazor workflow tree surface composing the surface behavior, grid decorator,
/// minimap, and a pooled node/link view layer. Set <see cref="Tree"/> to an
/// <see cref="IWorkflowTreeViewModel"/> to render; re-rendering, slot enumeration and the
/// default palette are supplied by the surface behavior. Override <see cref="NodeTemplate"/>
/// to replace the generated node card.
/// </summary>
public partial class TreeView : ComponentBase
{
    /// <summary>Gets or sets the workflow tree rendered by this surface.</summary>
    [Parameter]
    public IWorkflowTreeViewModel? Tree { get; set; }

    /// <summary>Gets or sets the scroll container element id.</summary>
    [Parameter]
    public string ScrollViewerId { get; set; } = "veloxdev-wf-scroll";

    /// <summary>Gets or sets the canvas element id.</summary>
    [Parameter]
    public string CanvasId { get; set; } = "veloxdev-wf-canvas";

    /// <summary>Gets or sets an optional per-node template (overrides the generated <c>NodeView</c>).</summary>
    [Parameter]
    public RenderFragment<IWorkflowNodeViewModel>? NodeTemplate { get; set; }

    /// <summary>Gets or sets the minor grid spacing in pixels.</summary>
    [Parameter]
    public double GridSpacing { get; set; } = 40;
}
