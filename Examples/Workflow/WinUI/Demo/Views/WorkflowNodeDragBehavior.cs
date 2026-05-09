using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VeloxDev.WorkflowSystem;

namespace Demo.Views;

public sealed class WorkflowNodeDragBehavior : DependencyObject
{
    private sealed class DragState
    {
        public bool IsDragging { get; set; }
        public Windows.Foundation.Point StartPosition { get; set; }
        public FrameworkElement? CoordinateHost { get; set; }
        public UserControl? Owner { get; set; }
        public UIElement? DragHandle { get; set; }
        public Vector3 BaseTranslation { get; set; }
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
        if (sender is not UIElement element || element.GetValue(StateProperty) is not DragState state)
        {
            return;
        }

        var point = e.GetCurrentPoint(element);
        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        state.Owner = ResolveOwner(element);
        if (state.Owner is null)
        {
            return;
        }

        state.CoordinateHost = ResolveCoordinateHost(element, state.Owner);
        if (state.CoordinateHost is null)
        {
            return;
        }

        state.DragHandle = element;
        state.StartPosition = e.GetCurrentPoint(state.CoordinateHost).Position;
        state.BaseTranslation = state.Owner.Translation;
        UpdateDragTranslation(state, 0, 0);
        state.IsDragging = true;
        element.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private static void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement || sender is not DependencyObject source || source.GetValue(StateProperty) is not DragState state || !state.IsDragging || state.CoordinateHost is null || state.Owner is null)
        {
            return;
        }

        var current = e.GetCurrentPoint(state.CoordinateHost).Position;
        UpdateDragTranslation(state, current.X - state.StartPosition.X, current.Y - state.StartPosition.Y);
        WorkflowSlotLayoutBehavior.RefreshNow(state.Owner);
        e.Handled = true;
    }

    private static void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not UIElement element || element.GetValue(StateProperty) is not DragState state || !state.IsDragging)
        {
            return;
        }

        var coordinateHost = state.CoordinateHost;
        var owner = state.Owner;
        var node = sender as DependencyObject is { } source ? ResolveNode(source) : null;
        if (coordinateHost is not null && owner is not null && node is not null)
        {
            var current = e.GetCurrentPoint(coordinateHost).Position;
            var delta = new Offset(current.X - state.StartPosition.X, current.Y - state.StartPosition.Y);
            if (Math.Abs(delta.Horizontal) > double.Epsilon || Math.Abs(delta.Vertical) > double.Epsilon)
            {
                node.MoveCommand.Execute(delta);
            }
        }

        state.IsDragging = false;
        RestoreOwnerTranslation(state);
        if (owner is not null)
        {
            WorkflowSlotLayoutBehavior.Refresh(owner);
        }

        element.ReleasePointerCapture(e.Pointer);
        state.CoordinateHost = null;
        state.Owner = null;
        state.DragHandle = null;
        e.Handled = true;
    }

    private static void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not DependencyObject source || source.GetValue(StateProperty) is not DragState state)
        {
            return;
        }

        state.IsDragging = false;
        RestoreOwnerTranslation(state);
        if (state.Owner is not null)
        {
            WorkflowSlotLayoutBehavior.Refresh(state.Owner);
        }

        state.CoordinateHost = null;
        state.Owner = null;
        state.DragHandle = null;
    }

    private static void UpdateDragTranslation(DragState state, double x, double y)
    {
        if (state.Owner is null)
        {
            return;
        }

        state.Owner.Translation = state.BaseTranslation + new Vector3((float)x, (float)y, 0f);
    }

    private static void RestoreOwnerTranslation(DragState state)
    {
        if (state.Owner is not null)
        {
            state.Owner.Translation = state.BaseTranslation;
        }

        state.BaseTranslation = default;
    }

    private static UserControl? ResolveOwner(DependencyObject source)
        => EnumerateVisualAncestors(source).OfType<UserControl>().FirstOrDefault();

    private static FrameworkElement? ResolveCoordinateHost(DependencyObject source, UserControl owner)
    {
        var hostName = GetCoordinateHostName(source);
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            var namedHost = ResolveNamedHost(source, hostName);
            if (namedHost is not null)
            {
                return namedHost;
            }
        }

        var hostType = GetCoordinateHostType(source) ?? typeof(Canvas);
        return EnumerateVisualAncestors(owner).OfType<FrameworkElement>().FirstOrDefault(x => hostType.IsAssignableFrom(x.GetType()));
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
