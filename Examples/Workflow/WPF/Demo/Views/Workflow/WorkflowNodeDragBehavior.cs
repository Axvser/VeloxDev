using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow
{
public sealed class WorkflowNodeDragBehavior : DependencyObject
{
    private sealed class DragState
    {
        public bool IsDragging { get; set; }
        public Point LastPosition { get; set; }
        public IInputElement? CoordinateHost { get; set; }
    }

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowNodeDragBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty CoordinateHostNameProperty = DependencyProperty.RegisterAttached(
        "CoordinateHostName",
        typeof(string),
        typeof(WorkflowNodeDragBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CoordinateHostTypeProperty = DependencyProperty.RegisterAttached(
        "CoordinateHostType",
        typeof(Type),
        typeof(WorkflowNodeDragBehavior),
        new PropertyMetadata(null));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(DragState),
        typeof(WorkflowNodeDragBehavior),
        new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static string? GetCoordinateHostName(DependencyObject element) => (string?)element.GetValue(CoordinateHostNameProperty);
    public static void SetCoordinateHostName(DependencyObject element, string? value) => element.SetValue(CoordinateHostNameProperty, value);

    public static Type? GetCoordinateHostType(DependencyObject element) => (Type?)element.GetValue(CoordinateHostTypeProperty);
    public static void SetCoordinateHostType(DependencyObject element, Type? value) => element.SetValue(CoordinateHostTypeProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if (Equals(e.NewValue, true))
        {
            Attach(element);
            return;
        }

        Detach(element);
    }

    private static void Attach(UIElement element)
    {
        Detach(element);
        element.SetValue(StateProperty, new DragState());
        element.PreviewMouseLeftButtonDown += OnMouseDown;
        element.PreviewMouseMove += OnMouseMove;
        element.PreviewMouseLeftButtonUp += OnMouseUp;
        element.LostMouseCapture += OnLostMouseCapture;
    }

    private static void Detach(UIElement element)
    {
        element.PreviewMouseLeftButtonDown -= OnMouseDown;
        element.PreviewMouseMove -= OnMouseMove;
        element.PreviewMouseLeftButtonUp -= OnMouseUp;
        element.LostMouseCapture -= OnLostMouseCapture;
        element.ClearValue(StateProperty);
    }

    private static void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement control || control.GetValue(StateProperty) is not DragState state)
        {
            return;
        }

        state.CoordinateHost = ResolveCoordinateHost(control);
        if (state.CoordinateHost is null)
        {
            return;
        }

        state.IsDragging = true;
        state.LastPosition = e.GetPosition(state.CoordinateHost);
        Mouse.Capture(control);
        e.Handled = true;
    }

    private static void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement control || control.GetValue(StateProperty) is not DragState state || !state.IsDragging || state.CoordinateHost is null)
        {
            return;
        }

        var node = ResolveNode(control);
        if (node is null)
        {
            return;
        }

        var current = e.GetPosition(state.CoordinateHost);
        node.MoveCommand.Execute(new Offset(current.X - state.LastPosition.X, current.Y - state.LastPosition.Y));
        state.LastPosition = current;
        e.Handled = true;
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement element || element.GetValue(StateProperty) is not DragState state || !state.IsDragging)
        {
            return;
        }

        state.IsDragging = false;
        state.CoordinateHost = null;
        Mouse.Capture(null);
        e.Handled = true;
    }

    private static void OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (sender is not UIElement element || element.GetValue(StateProperty) is not DragState state)
        {
            return;
        }

        state.IsDragging = false;
        state.CoordinateHost = null;
    }

    private static IInputElement? ResolveCoordinateHost(FrameworkElement control)
    {
        var hostName = GetCoordinateHostName(control);
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            var namedHost = ResolveNamedHost(control, hostName);
            if (namedHost is not null)
            {
                return namedHost;
            }
        }

        var hostType = GetCoordinateHostType(control) ?? typeof(Canvas);
        return EnumerateVisualAncestors(control).OfType<FrameworkElement>().FirstOrDefault(x => hostType.IsAssignableFrom(x.GetType()));
    }

    private static FrameworkElement? ResolveNamedHost(FrameworkElement control, string hostName)
    {
        if (control.Name == hostName)
        {
            return control;
        }

        return EnumerateVisualAncestors(control).OfType<FrameworkElement>().FirstOrDefault(x => x.Name == hostName);
    }

    private static IWorkflowNodeViewModel? ResolveNode(FrameworkElement control)
    {
        if (control.DataContext is IWorkflowNodeViewModel node)
        {
            return node;
        }

        return EnumerateVisualAncestors(control)
            .OfType<FrameworkElement>()
            .Select(static x => x.DataContext)
            .OfType<IWorkflowNodeViewModel>()
            .FirstOrDefault();
    }

    private static System.Collections.Generic.IEnumerable<DependencyObject> EnumerateVisualAncestors(DependencyObject source)
    {
        var current = VisualTreeHelper.GetParent(source);
        while (current is not null)
        {
            yield return current;
            current = VisualTreeHelper.GetParent(current);
        }
    }
}
}
