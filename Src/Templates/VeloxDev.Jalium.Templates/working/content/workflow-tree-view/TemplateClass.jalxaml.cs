// VeloxDev customization: The surface's wiring lives in WorkflowSurfaceBehavior; add code here only for host-level policy.
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace TemplateNamespace;

public partial class TemplateClass : UserControl
{
    /// <summary>The world-to-view translation the pooled node and link views follow.</summary>
    /// <remarks>This platform cannot bind an attached property, so the template binds this CLR mirror.</remarks>
    public static readonly DependencyProperty CanvasTransformProperty = WorkflowSurfaceBehavior.CanvasTransformProperty;

    /// <summary>The world-to-view translation the pooled node and link views follow.</summary>
    public Transform? CanvasTransform => GetValue(CanvasTransformProperty) as Transform;

    public TemplateClass() => InitializeComponent();
}
