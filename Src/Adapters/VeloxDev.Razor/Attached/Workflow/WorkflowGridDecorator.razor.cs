using Microsoft.AspNetCore.Components;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Ruler bars (grid decorator) that mirror the scroll/content offset of a
/// <see cref="WorkflowSurfaceBehavior"/>. Consumes a <see cref="SurfaceViewport"/>
/// context and implements <see cref="IWorkflowGridDecorator"/> for API parity with the
/// XAML adapters.
/// </summary>
public partial class WorkflowGridDecorator : ComponentBase, IWorkflowGridDecorator
{
    /// <summary>Gets or sets the surface viewport context pushed by <see cref="WorkflowSurfaceBehavior"/>.</summary>
    [Parameter]
    public SurfaceViewport? Viewport { get; set; }

    /// <summary>Gets or sets the ruler thickness in pixels.</summary>
    [Parameter]
    public double RulerThickness { get; set; } = 28;

    /// <summary>Gets or sets the tick spacing in pixels.</summary>
    [Parameter]
    public double Spacing { get; set; } = 40;

    /// <summary>Gets or sets the ruler background color.</summary>
    [Parameter]
    public string RulerBackground { get; set; } = "#252526";

    /// <summary>Gets or sets the tick color.</summary>
    [Parameter]
    public string TickColor { get; set; } = "#555555";

    /// <summary>Gets or sets the label color (reserved for future numeric labels).</summary>
    [Parameter]
    public string LabelColor { get; set; } = "#888888";

    /// <summary>Gets or sets the ruler divider color.</summary>
    [Parameter]
    public string DividerColor { get; set; } = "#3A3D40";

    /// <inheritdoc />
    public double ScrollOffsetX { get; set; }

    /// <inheritdoc />
    public double ScrollOffsetY { get; set; }

    /// <inheritdoc />
    public double ContentOffsetX { get; set; }

    /// <inheritdoc />
    public double ContentOffsetY { get; set; }

    private string RulerThicknessCss => RulerThickness.ToString("0.#");
    private string SpacingCss => Spacing.ToString("0.#");
    private string TopTransform => $"translateX({(-ScrollOffsetX).ToString("0.#")}px)";
    private string LeftTransform => $"translateY({(-ScrollOffsetY).ToString("0.#")}px)";

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        if (Viewport is { } vp)
        {
            ScrollOffsetX = vp.ScrollLeft;
            ScrollOffsetY = vp.ScrollTop;
            ContentOffsetX = vp.ContentOffsetX;
            ContentOffsetY = vp.ContentOffsetY;
        }
    }
}
