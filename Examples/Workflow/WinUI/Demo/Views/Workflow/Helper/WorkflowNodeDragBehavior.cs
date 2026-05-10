using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace Demo.Views;

public sealed class WorkflowNodeDragBehavior : DependencyObject
{
    private sealed class DragState
    {
        public bool IsDragging { get; set; }
        public Windows.Foundation.Point LastPosition { get; set; }
        public FrameworkElement? CoordinateHost { get; set; }
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

    public static string? GetCoordinateHostName(DependencyObject element) => element.GetValue(CoordinateHostNameProperty) as string;

    public static void SetCoordinateHostName(DependencyObject element, string? value) => element.SetValue(CoordinateHostNameProperty, value);

    public static Type? GetCoordinateHostType(DependencyObject element) => element.GetValue(CoordinateHostTypeProperty) as Type;

    public static void SetCoordinateHostType(DependencyObject element, Type? value) => element.SetValue(CoordinateHostTypeProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        if (e.NewValue is true)
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
        element.PointerPressed += OnPointerPressed;
        element.PointerMoved += OnPointerMoved;
        element.PointerReleased += OnPointerReleased;
        element.PointerCaptureLost += OnPointerCaptureLost;
    }

    private static void Detach(UIElement element)
    {
        element.PointerPressed -= OnPointerPressed;
        element.PointerMoved -= OnPointerMoved;
        element.PointerReleased -= OnPointerReleased;
        element.PointerCaptureLost -= OnPointerCaptureLost;
        element.ClearValue(StateProperty);
    }

    private static void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not FrameworkElement control || control.GetValue(StateProperty) is not DragState state)
        {
            return;
        }

        var point = e.GetCurrentPoint(control);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        state.CoordinateHost = ResolveCoordinateHost(control);
        if (state.CoordinateHost is null)
        {
            return;
        }

        state.LastPosition = e.GetCurrentPoint(state.CoordinateHost).Position;
        state.IsDragging = true;
        control.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private static void OnPointerMoved(object sender, PointerRoutedEventArgs e)
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

        var current = e.GetCurrentPoint(state.CoordinateHost).Position;
        node.MoveCommand.Execute(new Offset(current.X - state.LastPosition.X, current.Y - state.LastPosition.Y));
        state.LastPosition = current;
        e.Handled = true;
    }

    private static void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element || element.GetValue(StateProperty) is not DragState state || !state.IsDragging)
        {
            return;
        }

        state.IsDragging = false;
        state.CoordinateHost = null;
        element.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    private static void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not DependencyObject source || source.GetValue(StateProperty) is not DragState state)
        {
            return;
        }

        state.IsDragging = false;
        state.CoordinateHost = null;
    }

    private static FrameworkElement? ResolveCoordinateHost(FrameworkElement control)
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

    private static FrameworkElement? ResolveNamedHost(DependencyObject source, string hostName)
    {
        foreach (var candidate in EnumerateSelfAndAncestors(source).OfType<FrameworkElement>())
        {
            if (candidate.Name == hostName)
            {
                return candidate;
            }
        }

        return null;
    }

    private static IWorkflowNodeViewModel? ResolveNode(DependencyObject source)
    {
        foreach (var candidate in EnumerateSelfAndAncestors(source).OfType<FrameworkElement>())
        {
            if (candidate.DataContext is IWorkflowNodeViewModel node)
            {
                return node;
            }
        }

        return null;
    }

    private static IEnumerable<DependencyObject> EnumerateSelfAndAncestors(DependencyObject source)
    {
        yield return source;
        foreach (var ancestor in EnumerateVisualAncestors(source))
        {
            yield return ancestor;
        }
    }

    private static IEnumerable<DependencyObject> EnumerateVisualAncestors(DependencyObject source)
    {
        var current = VisualTreeHelper.GetParent(source);
        while (current is not null)
        {
            yield return current;
            current = VisualTreeHelper.GetParent(current);
        }
    }
}
