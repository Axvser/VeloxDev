using System;
using System.Collections.Generic;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Attached behavior that turns a node view into a drag handle: a left-press followed by a move issues
/// <see cref="IWorkflowNodeViewModel.MoveCommand"/> with the offset since the last move.
/// </summary>
/// <remarks>
/// <para>
/// Attach it to the element that should start a drag — usually a card's title bar rather than the whole card, so
/// that dragging a port does not also drag the node:
/// <c>behaviors:WorkflowNodeDragBehavior.IsEnabled="True"</c>.
/// </para>
/// <para>
/// The behavior does not decide where the node ends up: it measures against the coordinate host and hands the
/// delta to the model. The host is resolved by <see cref="CoordinateHostNameProperty"/> first and by
/// <see cref="CoordinateHostTypeProperty"/> (default <c>Canvas</c>) after that, walking up the visual tree.
/// </para>
/// </remarks>
public sealed class WorkflowNodeDragBehavior : DependencyObject
{
    private sealed class DragState
    {
        public bool IsDragging { get; set; }

        public Point LastPosition { get; set; }

        public FrameworkElement? CoordinateHost { get; set; }
    }

    /// <summary>The attached property that turns node dragging on for an element.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowNodeDragBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>The attached property naming the element the drag is measured against.</summary>
    public static readonly DependencyProperty CoordinateHostNameProperty = DependencyProperty.RegisterAttached(
        "CoordinateHostName",
        typeof(string),
        typeof(WorkflowNodeDragBehavior),
        new PropertyMetadata(null));

    /// <summary>The attached property giving the type of element the drag is measured against.</summary>
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

    /// <summary>Reads the <c>IsEnabled</c> attached property from <paramref name="element"/>.</summary>
    public static bool GetIsEnabled(DependencyObject element) => element.GetValue(IsEnabledProperty) is true;

    /// <summary>Sets the <c>IsEnabled</c> attached property on <paramref name="element"/>.</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>Reads the <c>CoordinateHostName</c> attached property from <paramref name="element"/>.</summary>
    public static string? GetCoordinateHostName(DependencyObject element) => (string?)element.GetValue(CoordinateHostNameProperty);

    /// <summary>Sets the <c>CoordinateHostName</c> attached property on <paramref name="element"/>.</summary>
    public static void SetCoordinateHostName(DependencyObject element, string? value) => element.SetValue(CoordinateHostNameProperty, value);

    /// <summary>Reads the <c>CoordinateHostType</c> attached property from <paramref name="element"/>.</summary>
    public static Type? GetCoordinateHostType(DependencyObject element) => (Type?)element.GetValue(CoordinateHostTypeProperty);

    /// <summary>Sets the <c>CoordinateHostType</c> attached property on <paramref name="element"/>.</summary>
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

    private static void OnMouseDown(object? sender, MouseButtonEventArgs e)
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
        control.CaptureMouse();
        e.Handled = true;
    }

    private static void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement control
            || control.GetValue(StateProperty) is not DragState state
            || !state.IsDragging
            || state.CoordinateHost is null)
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

    private static void OnMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not UIElement element || element.GetValue(StateProperty) is not DragState state || !state.IsDragging)
        {
            return;
        }

        state.IsDragging = false;
        state.CoordinateHost = null;
        element.ReleaseMouseCapture();
        e.Handled = true;
    }

    private static void OnLostMouseCapture(object? sender, MouseEventArgs e)
    {
        if (sender is not UIElement element || element.GetValue(StateProperty) is not DragState state)
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
            var namedHost = ResolveNamedHost(control, hostName!);
            if (namedHost is not null)
            {
                return namedHost;
            }
        }

        var hostType = GetCoordinateHostType(control) ?? typeof(Canvas);
        foreach (var ancestor in EnumerateVisualAncestors(control))
        {
            if (ancestor is FrameworkElement element && hostType.IsInstanceOfType(element))
            {
                return element;
            }
        }

        return null;
    }

    // 按名字找宿主：先看自己，再沿视觉树向上比 Name。
    // 不查名字作用域 —— 节点视图住在 DataTemplate 里，模板内的元素不在表面的作用域内。
    private static FrameworkElement? ResolveNamedHost(FrameworkElement control, string hostName)
    {
        if (control.Name == hostName)
        {
            return control;
        }

        foreach (var ancestor in EnumerateVisualAncestors(control))
        {
            if (ancestor is FrameworkElement element && element.Name == hostName)
            {
                return element;
            }
        }

        return null;
    }

    private static IWorkflowNodeViewModel? ResolveNode(FrameworkElement control)
    {
        if (control.DataContext is IWorkflowNodeViewModel node)
        {
            return node;
        }

        foreach (var ancestor in EnumerateVisualAncestors(control))
        {
            if (ancestor is FrameworkElement element && element.DataContext is IWorkflowNodeViewModel resolved)
            {
                return resolved;
            }
        }

        return null;
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
