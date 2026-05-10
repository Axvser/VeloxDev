using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public sealed class WorkflowSlotConnectionBehavior
{
    internal static bool IsDraggingConnection { get; private set; }

    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSlotConnectionBehavior),
        false,
        propertyChanged: OnIsEnabledChanged);

    internal static void SetIsDraggingConnection(bool isDraggingConnection) => IsDraggingConnection = isDraggingConnection;

    public static bool GetIsEnabled(BindableObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(BindableObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not View view)
        {
            return;
        }

        foreach (var gesture in view.GestureRecognizers.OfType<TapGestureRecognizer>().ToArray())
        {
            gesture.Tapped -= OnTapped;
            view.GestureRecognizers.Remove(gesture);
        }

        if (newValue is true)
        {
            var gesture = new TapGestureRecognizer();
            gesture.Tapped += OnTapped;
            view.GestureRecognizers.Add(gesture);
        }
    }

    private static void OnTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not BindableObject { BindingContext: IWorkflowSlotViewModel slot } bindable)
        {
            return;
        }

        var host = FindHost(bindable);
        if (host is null)
        {
            return;
        }

        host.BeginConnection(slot);
    }

    private static IWorkflowSurfaceHost? FindHost(BindableObject bindable)
    {
        Element? current = bindable as Element;
        while (current is not null)
        {
            if (current is IWorkflowSurfaceHost host)
            {
                return host;
            }

            current = current.Parent;
        }

        return null;
    }
}
