using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Attached behavior that owns the canvas render transform for the workflow surface host.
/// WorkflowSurfaceBehavior sets this property directly instead of using reflection.
/// </summary>
public sealed class WorkflowCanvasTransformBehavior : AvaloniaObject
{
    public static readonly AttachedProperty<ITransform?> TransformProperty =
        AvaloniaProperty.RegisterAttached<WorkflowCanvasTransformBehavior, Control, ITransform?>("Transform");

    static WorkflowCanvasTransformBehavior()
    {
        TransformProperty.Changed.AddClassHandler<Control>(OnTransformChanged);
    }

    public static ITransform? GetTransform(AvaloniaObject element) => element.GetValue(TransformProperty);

    public static void SetTransform(AvaloniaObject element, ITransform? value) => element.SetValue(TransformProperty, value);

    internal static void Apply(Control element, ITransform transform)
        => element.SetValue(TransformProperty, transform);

    private static void OnTransformChanged(Control element, AvaloniaPropertyChangedEventArgs e)
    {
        // 有意留空：此属性只作通知载体。
        // 节点/连线视图各自把自己的 RenderTransform 绑到它上面，宿主本身不该收到渲染变换。
    }
}
