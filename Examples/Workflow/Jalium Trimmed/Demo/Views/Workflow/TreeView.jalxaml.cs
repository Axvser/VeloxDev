// VeloxDev customization: The surface's wiring lives in WorkflowSurfaceBehavior; add code here only for host-level policy.
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

public partial class TreeView : UserControl
{
    /// <summary>
    /// The world-to-view translation the pooled node and link views follow.
    /// </summary>
    /// <remarks>
    /// It is the surface's <see cref="WorkflowSurfaceBehavior.CanvasTransform"/> re-exposed under a name a
    /// binding can address — this platform's bindings cannot read an attached property, so the templates bind
    /// this CLR property instead. Same value, same notifications; only the way it is spelled differs.
    /// </remarks>
    public static readonly DependencyProperty CanvasTransformProperty = WorkflowSurfaceBehavior.CanvasTransformProperty;

    /// <summary>The world-to-view translation the pooled node and link views follow.</summary>
    public Transform? CanvasTransform => GetValue(CanvasTransformProperty) as Transform;

    public TreeView() => InitializeComponent();
}
