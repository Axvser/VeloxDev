using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Attached behavior that owns the canvas render transform for the workflow surface host.
/// WorkflowSurfaceBehavior sets this property directly instead of using reflection.
/// </summary>
public static class WorkflowCanvasTransformBehavior
{
    public static readonly DependencyProperty TransformProperty = DependencyProperty.RegisterAttached(
        "Transform",
        typeof(Transform),
        typeof(WorkflowCanvasTransformBehavior),
        new PropertyMetadata(null, OnTransformChanged));

    public static Transform? GetTransform(UIElement element) => (Transform?)element.GetValue(TransformProperty);

    public static void SetTransform(UIElement element, Transform? value) => element.SetValue(TransformProperty, value);

    internal static void Apply(UIElement element, Transform transform)
        => element.SetValue(TransformProperty, transform);

    private static void OnTransformChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // 有意留空：此属性只作通知载体。
        // 节点/连线视图各自把自己的 RenderTransform 绑到它上面，宿主本身不该收到渲染变换。
    }
}
