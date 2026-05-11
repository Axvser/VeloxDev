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
        // Intentionally empty: this property is a notification carrier only.
        // Node and link views bind their own RenderTransform to this attached property via XAML.
        // The host itself must not receive a render transform.
    }
}
