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
        // Intentionally empty: this property is a notification carrier only.
        // Node and link views bind their own RenderTransform to this attached property via XAML.
        // The host itself must not receive a render transform.
    }
}
