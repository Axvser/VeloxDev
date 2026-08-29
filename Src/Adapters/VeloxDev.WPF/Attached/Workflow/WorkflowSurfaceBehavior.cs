using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors
{
public sealed class WorkflowSurfaceBehavior : DependencyObject
{
    private static readonly MouseButtonEventHandler MouseUpHandler = OnMouseUp;

    private sealed class SurfaceState
    {
        public bool IsPanning { get; set; }
        public Point PanStart { get; set; }
        public Vector PanStartOffset { get; set; }
        public ScrollViewer? ScrollViewer { get; set; }
        public Canvas? Canvas { get; set; }
        public FrameworkElement? GridDecorator { get; set; }
        public FrameworkElement? MinimapOverlay { get; set; }
        public FrameworkElement? PointerPressSource { get; set; }
    }

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty ScrollViewerNameProperty = DependencyProperty.RegisterAttached(
        "ScrollViewerName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty CanvasNameProperty = DependencyProperty.RegisterAttached(
        "CanvasName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty GridDecoratorNameProperty = DependencyProperty.RegisterAttached(
        "GridDecoratorName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty PointerPressSourceNameProperty = DependencyProperty.RegisterAttached(
        "PointerPressSourceName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty MinimapOverlayNameProperty = DependencyProperty.RegisterAttached(
        "MinimapOverlayName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State",
        typeof(SurfaceState),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static string? GetScrollViewerName(DependencyObject element) => (string?)element.GetValue(ScrollViewerNameProperty);
    public static void SetScrollViewerName(DependencyObject element, string? value) => element.SetValue(ScrollViewerNameProperty, value);

    public static string? GetCanvasName(DependencyObject element) => (string?)element.GetValue(CanvasNameProperty);
    public static void SetCanvasName(DependencyObject element, string? value) => element.SetValue(CanvasNameProperty, value);

    public static string? GetGridDecoratorName(DependencyObject element) => (string?)element.GetValue(GridDecoratorNameProperty);
    public static void SetGridDecoratorName(DependencyObject element, string? value) => element.SetValue(GridDecoratorNameProperty, value);

    public static string? GetPointerPressSourceName(DependencyObject element) => (string?)element.GetValue(PointerPressSourceNameProperty);
    public static void SetPointerPressSourceName(DependencyObject element, string? value) => element.SetValue(PointerPressSourceNameProperty, value);

    public static string? GetMinimapOverlayName(DependencyObject element) => (string?)element.GetValue(MinimapOverlayNameProperty);
    public static void SetMinimapOverlayName(DependencyObject element, string? value) => element.SetValue(MinimapOverlayNameProperty, value);

    public static void Refresh(UserControl host)
    {
        if (!GetIsEnabled(host))
        {
            return;
        }

        var state = (SurfaceState?)host.GetValue(StateProperty) ?? new SurfaceState();
        host.SetValue(StateProperty, state);
        ResolveNamedControls(host, state);
        ApplyLayout(host, state);
        UpdateVisibleRegion(host, state);
    }

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UserControl control)
        {
            return;
        }

        if (Equals(e.NewValue, true))
        {
            Attach(control);
            return;
        }

        Detach(control);
    }

    private static void Attach(UserControl control)
    {
        Detach(control);

        var state = new SurfaceState();
        control.SetValue(StateProperty, state);
        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        control.DataContextChanged += OnDataContextChanged;
        control.PreviewMouseMove += OnPreviewMouseMove;
        control.AddHandler(UIElement.MouseUpEvent, MouseUpHandler, true);
        ResolveNamedControls(control, state);
        Refresh(control);
    }

    private static void Detach(UserControl control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.DataContextChanged -= OnDataContextChanged;
        control.PreviewMouseMove -= OnPreviewMouseMove;
        control.RemoveHandler(UIElement.MouseUpEvent, MouseUpHandler);

        if (control.GetValue(StateProperty) is SurfaceState state)
        {
            UnsubscribeResolvedControls(state);
        }

        control.ClearValue(StateProperty);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is UserControl control)
        {
            Refresh(control);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is UserControl control && control.GetValue(StateProperty) is SurfaceState state)
        {
            state.IsPanning = false;
        }
    }

    private static void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is UserControl control)
        {
            Refresh(control);
        }
    }

    private static void ResolveNamedControls(UserControl control, SurfaceState state)
    {
        UnsubscribeResolvedControls(state);

        var scrollViewerName = GetScrollViewerName(control);
        if (!string.IsNullOrWhiteSpace(scrollViewerName))
        {
            state.ScrollViewer = control.FindName(scrollViewerName) as ScrollViewer;
        }

        var canvasName = GetCanvasName(control);
        if (!string.IsNullOrWhiteSpace(canvasName))
        {
            state.Canvas = control.FindName(canvasName) as Canvas;
        }

        var gridDecoratorName = GetGridDecoratorName(control);
        if (!string.IsNullOrWhiteSpace(gridDecoratorName))
        {
            state.GridDecorator = control.FindName(gridDecoratorName) as FrameworkElement;
        }

        var minimapOverlayName = GetMinimapOverlayName(control);
        if (!string.IsNullOrWhiteSpace(minimapOverlayName))
        {
            state.MinimapOverlay = control.FindName(minimapOverlayName) as FrameworkElement;
        }

        var pointerPressSourceName = GetPointerPressSourceName(control);
        if (!string.IsNullOrWhiteSpace(pointerPressSourceName))
        {
            state.PointerPressSource = control.FindName(pointerPressSourceName) as FrameworkElement;
        }

        if (state.PointerPressSource is not null)
        {
            state.PointerPressSource.PreviewMouseDown += OnPointerPressed;
            state.PointerPressSource.PreviewMouseLeftButtonUp += OnSurfaceMouseLeftButtonUp;
        }

        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.ScrollChanged += OnScrollChanged;
        }
    }

    private static void UnsubscribeResolvedControls(SurfaceState state)
    {
        if (state.PointerPressSource is not null)
        {
            state.PointerPressSource.PreviewMouseDown -= OnPointerPressed;
            state.PointerPressSource.PreviewMouseLeftButtonUp -= OnSurfaceMouseLeftButtonUp;
        }

        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.ScrollChanged -= OnScrollChanged;
        }

        state.PointerPressSource = null;
        state.ScrollViewer = null;
        state.Canvas = null;
        state.GridDecorator = null;
        state.MinimapOverlay = null;
    }

    private static void OnPointerPressed(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement source)
        {
            return;
        }

        var host = EnumerateVisualAncestors(source).OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host is null || host.GetValue(StateProperty) is not SurfaceState state || state.ScrollViewer is null)
        {
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        // Start canvas panning only when the click lands on blank background (not on nodes/slots/links or other interactive elements).
        if (e.OriginalSource is not DependencyObject originalSource
            || !IsSurfaceBlankInteraction(originalSource, state))
        {
            return;
        }

        state.IsPanning = true;
        state.PanStart = e.GetPosition(host);
        state.PanStartOffset = new Vector(state.ScrollViewer.HorizontalOffset, state.ScrollViewer.VerticalOffset);
        Mouse.Capture(source);
        e.Handled = true;
    }

    private static void OnSurfaceMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement source)
        {
            return;
        }

        var host = EnumerateVisualAncestors(source).OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host is null || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel || !viewModel.VirtualLink.IsVisible)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject originalSource)
        {
            return;
        }

        if (!IsSurfaceBlankInteraction(originalSource, state))
        {
            return;
        }

        viewModel.ResetVirtualLinkCommand.Execute(null);
        e.Handled = true;
    }

    private static void OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        OnCanvasPanMoved(host, state, e);
        if (state.IsPanning)
        {
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.Canvas is null)
        {
            return;
        }

        var point = e.GetPosition(state.Canvas);
        viewModel.SetPointerCommand.Execute(
            WorkflowSurfaceMath.ToWorldAnchor(point.X, point.Y, 0, viewModel.Layout));
    }

    private static void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (state.IsPanning)
        {
            state.IsPanning = false;
            Mouse.Capture(null);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }
    }

    private static void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
        {
            return;
        }

        var host = EnumerateVisualAncestors(viewer).OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host is not null)
        {
            Refresh(host);
        }
    }

    private static void OnCanvasPanMoved(UserControl host, SurfaceState state, MouseEventArgs e)
    {
        if (!state.IsPanning || state.ScrollViewer is null || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            state.IsPanning = false;
            if (Mouse.Captured is not null)
            {
                Mouse.Capture(null);
            }

            return;
        }

        var current = e.GetPosition(host);
        var desiredX = state.PanStartOffset.X + (state.PanStart.X - current.X);
        var desiredY = state.PanStartOffset.Y + (state.PanStart.Y - current.Y);
        var maxH = GetHorizontalScrollMaximum(state.ScrollViewer);
        var maxV = GetVerticalScrollMaximum(state.ScrollViewer);

        // Canonical overscroll clamp: expands the canvas via Negative/PositiveOffset when panning past
        // the content edge, then returns the clamped scroll offset (WorkflowSurfaceMath.ClampScrollOffset).
        var newOffsetX = WorkflowSurfaceMath.ClampScrollOffset(desiredX, maxH, viewModel.Layout, horizontal: true);
        var newOffsetY = WorkflowSurfaceMath.ClampScrollOffset(desiredY, maxV, viewModel.Layout, horizontal: false);
        var layoutChanged = newOffsetX != desiredX || newOffsetY != desiredY;

        if (layoutChanged)
        {
            ApplyLayout(host, state);
            state.Canvas?.UpdateLayout();
            state.ScrollViewer.UpdateLayout();
            host.UpdateLayout();
            maxH = GetHorizontalScrollMaximum(state.ScrollViewer);
            maxV = GetVerticalScrollMaximum(state.ScrollViewer);
            newOffsetX = Math.Min(newOffsetX, maxH);
            newOffsetY = Math.Min(newOffsetY, maxV);
            state.PanStart = current;
            state.PanStartOffset = new Vector(
                WorkflowSurfaceMath.ClampValue(newOffsetX, 0, maxH),
                WorkflowSurfaceMath.ClampValue(newOffsetY, 0, maxV));
        }

        var appliedOffsetX = WorkflowSurfaceMath.ClampValue(newOffsetX, 0, maxH);
        var appliedOffsetY = WorkflowSurfaceMath.ClampValue(newOffsetY, 0, maxV);

        state.ScrollViewer.ScrollToHorizontalOffset(appliedOffsetX);
        state.ScrollViewer.ScrollToVerticalOffset(appliedOffsetY);
        state.PanStart = current;
        state.PanStartOffset = new Vector(appliedOffsetX, appliedOffsetY);
        UpdateVisibleRegion(host, state);
        e.Handled = true;
    }

    private static void ApplyLayout(UserControl host, SurfaceState state)
    {
        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.Canvas is null)
        {
            return;
        }

        var transform = new TranslateTransform(
            viewModel.Layout.ActualOffset.Horizontal,
            viewModel.Layout.ActualOffset.Vertical);

        WorkflowCanvasTransformBehavior.Apply(host, transform);

        UpdateGridDecorator(viewModel, state);
        UpdateMinimapOverlay(viewModel, state);
    }

    private static void UpdateVisibleRegion(UserControl host, SurfaceState state)
    {
        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.ScrollViewer is null)
        {
            return;
        }

        UpdateGridDecorator(viewModel, state);
        UpdateMinimapOverlay(viewModel, state);
        var viewportX = WorkflowSurfaceMath.ToWorld(state.ScrollViewer.HorizontalOffset, viewModel.Layout.ActualOffset.Horizontal);
        var viewportY = WorkflowSurfaceMath.ToWorld(state.ScrollViewer.VerticalOffset, viewModel.Layout.ActualOffset.Vertical);
        viewModel.GetHelper().Viewport = new Viewport(
            viewportX, viewportY,
            state.ScrollViewer.ViewportWidth,
            state.ScrollViewer.ViewportHeight);

        // Persist the viewport position so it survives serialization round-trip.
        viewModel.Layout.ViewportOffset = new Offset(viewportX, viewportY);
    }

    private static void UpdateGridDecorator(IWorkflowTreeViewModel viewModel, SurfaceState state)
    {
        if (state.GridDecorator is null || state.ScrollViewer is null)
        {
            return;
        }

        if (state.GridDecorator is IWorkflowGridDecorator decorator)
        {
            decorator.ScrollOffsetX = state.ScrollViewer.HorizontalOffset;
            decorator.ScrollOffsetY = state.ScrollViewer.VerticalOffset;
            decorator.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
            decorator.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;
        }
    }

    private static void UpdateMinimapOverlay(IWorkflowTreeViewModel viewModel, SurfaceState state)
    {
        if (state.MinimapOverlay is not IWorkflowMinimapOverlay minimap || state.ScrollViewer is null)
        {
            return;
        }

        minimap.ScrollOffsetX = state.ScrollViewer.HorizontalOffset;
        minimap.ScrollOffsetY = state.ScrollViewer.VerticalOffset;
        minimap.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
        minimap.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;
        minimap.ViewportWidth = state.ScrollViewer.ViewportWidth;
        minimap.ViewportHeight = state.ScrollViewer.ViewportHeight;
        minimap.WorkflowTree = viewModel;
    }

    private static double GetHorizontalScrollMaximum(ScrollViewer scrollViewer)
        => WorkflowSurfaceMath.ScrollMax(scrollViewer.ExtentWidth, scrollViewer.ViewportWidth);

    private static double GetVerticalScrollMaximum(ScrollViewer scrollViewer)
        => WorkflowSurfaceMath.ScrollMax(scrollViewer.ExtentHeight, scrollViewer.ViewportHeight);

    private static bool IsSurfaceBlankInteraction(DependencyObject source, SurfaceState state)
    {
        if (IsWorkflowNodeOrSlotVisual(source))
        {
            return false;
        }

        var ancestors = EnumerateVisualAncestors(source).ToArray();
        if (ancestors.Any(IsWorkflowNodeOrSlotVisual))
        {
            return false;
        }

        // Do not start canvas panning when clicking the ScrollViewer's scroll bar (Thumb/Track/RepeatButton/ScrollBar).
        if (source is System.Windows.Controls.Primitives.ScrollBar
            || ancestors.Any(x => x is System.Windows.Controls.Primitives.ScrollBar))
        {
            return false;
        }

        if (IsWorkflowLinkVisual(source) || ancestors.Any(IsWorkflowLinkVisual))
        {
            return true;
        }

        return source == state.Canvas
            || source == state.ScrollViewer
            || source == state.PointerPressSource
            || source == state.GridDecorator
            || ancestors.Any(x => x == state.Canvas
                || x == state.ScrollViewer
                || x == state.PointerPressSource
                || x == state.GridDecorator
                || x is ScrollContentPresenter);
    }

    private static bool IsWorkflowNodeOrSlotVisual(DependencyObject source)
        => source is FrameworkElement { DataContext: IWorkflowNodeViewModel or IWorkflowSlotViewModel };

    private static bool IsWorkflowLinkVisual(DependencyObject source)
        => source is FrameworkElement element
            && (element.DataContext is IWorkflowLinkViewModel
                || string.Equals(element.GetType().Name, "BezierCurveView", StringComparison.Ordinal)
                || string.Equals(element.GetType().Name, "PolylineCurveView", StringComparison.Ordinal));

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