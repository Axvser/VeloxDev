using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>Attached to the workflow surface host. Drives pan (with auto-extend via
/// Layout.Positive/NegativeOffset), pushes the canvas content translate, updates the
/// grid/minimap and the tree viewport, and feeds SetPointerCommand during connection
/// drags.</summary>
public sealed class WorkflowSurfaceBehavior : DependencyObject
{
    private sealed class SurfaceState
    {
        public bool IsPanning { get; set; }
        public Point PanStart { get; set; }
        public Point PanStartOffset { get; set; }
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

    /// <summary>Re-resolves named parts and pushes offsets/viewport. Call after wiring a tree.</summary>
    public static void Refresh(FrameworkElement host)
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
        if (d is not FrameworkElement control)
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

    private static void Attach(FrameworkElement control)
    {
        Detach(control);

        var state = new SurfaceState();
        control.SetValue(StateProperty, state);
        control.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        control.AddHandler(FrameworkElement.UnloadedEvent, new RoutedEventHandler(OnUnloaded));
        control.DataContextChanged += OnDataContextChanged;
        control.PreviewMouseMove += OnPreviewMouseMove;
        control.AddHandler(UIElement.MouseUpEvent, new MouseButtonEventHandler(OnMouseUp));
        ResolveNamedControls(control, state);
        Refresh(control);
    }

    private static void Detach(FrameworkElement control)
    {
        control.RemoveHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnLoaded));
        control.RemoveHandler(FrameworkElement.UnloadedEvent, new RoutedEventHandler(OnUnloaded));
        control.DataContextChanged -= OnDataContextChanged;
        control.PreviewMouseMove -= OnPreviewMouseMove;
        control.RemoveHandler(UIElement.MouseUpEvent, new MouseButtonEventHandler(OnMouseUp));

        if (control.GetValue(StateProperty) is SurfaceState state)
        {
            UnsubscribeResolvedControls(state);
        }

        control.ClearValue(StateProperty);
    }

    private static void OnLoaded(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement control)
        {
            Refresh(control);
        }
    }

    private static void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement control && control.GetValue(StateProperty) is SurfaceState state)
        {
            state.IsPanning = false;
        }
    }

    private static void OnDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is FrameworkElement control)
        {
            Refresh(control);
        }
    }

    private static void ResolveNamedControls(FrameworkElement control, SurfaceState state)
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

    private static void OnPointerPressed(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement source)
        {
            return;
        }

        var host = EnumerateVisualAncestors(source).OfType<FrameworkElement>().FirstOrDefault(GetIsEnabled);
        if (host is null || host.GetValue(StateProperty) is not SurfaceState state || state.ScrollViewer is null)
        {
            return;
        }

        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject originalSource || !IsSurfaceBlankInteraction(originalSource, state))
        {
            return;
        }

        state.IsPanning = true;
        state.PanStart = e.GetPosition(host);
        state.PanStartOffset = new Point(state.ScrollViewer.HorizontalOffset, state.ScrollViewer.VerticalOffset);
        source.CaptureMouse();
        e.Handled = true;
    }

    private static void OnSurfaceMouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement source)
        {
            return;
        }

        var host = EnumerateVisualAncestors(source).OfType<FrameworkElement>().FirstOrDefault(GetIsEnabled);
        if (host is null || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (host.DataContext is not IWorkflowTreeViewModel viewModel || !viewModel.VirtualLink.IsVisible)
        {
            return;
        }

        if (e.OriginalSource is not DependencyObject originalSource || !IsSurfaceBlankInteraction(originalSource, state))
        {
            return;
        }

        viewModel.ResetVirtualLinkCommand.Execute(null);
        e.Handled = true;
    }

    private static void OnPreviewMouseMove(object? sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement host || host.GetValue(StateProperty) is not SurfaceState state)
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
        viewModel.SetPointerCommand.Execute(new Anchor(
            point.X - viewModel.Layout.ActualOffset.Horizontal,
            point.Y - viewModel.Layout.ActualOffset.Vertical,
            0));
    }

    private static void OnMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement host || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (state.IsPanning)
        {
            state.IsPanning = false;
            host.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
        {
            return;
        }

        var host = EnumerateVisualAncestors(viewer).OfType<FrameworkElement>().FirstOrDefault(GetIsEnabled);
        if (host is not null)
        {
            Refresh(host);
        }
    }

    private static void OnCanvasPanMoved(FrameworkElement host, SurfaceState state, MouseEventArgs e)
    {
        if (!state.IsPanning || state.ScrollViewer is null || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        if (e.LeftButton != MouseButtonState.Pressed)
        {
            state.IsPanning = false;
            host.ReleaseMouseCapture();
            return;
        }

        var current = e.GetPosition(host);
        var newOffsetX = state.PanStartOffset.X + (state.PanStart.X - current.X);
        var newOffsetY = state.PanStartOffset.Y + (state.PanStart.Y - current.Y);
        var maxH = state.ScrollViewer.ScrollableWidth;
        var maxV = state.ScrollViewer.ScrollableHeight;
        var layoutChanged = false;

        if (newOffsetX < 0)
        {
            viewModel.Layout.NegativeOffset += new Offset(-newOffsetX, 0);
            newOffsetX = 0;
            layoutChanged = true;
        }
        else if (newOffsetX > maxH)
        {
            viewModel.Layout.PositiveOffset += new Offset(newOffsetX - maxH, 0);
            newOffsetX = maxH;
            layoutChanged = true;
        }

        if (newOffsetY < 0)
        {
            viewModel.Layout.NegativeOffset += new Offset(0, -newOffsetY);
            newOffsetY = 0;
            layoutChanged = true;
        }
        else if (newOffsetY > maxV)
        {
            viewModel.Layout.PositiveOffset += new Offset(0, newOffsetY - maxV);
            newOffsetY = maxV;
            layoutChanged = true;
        }

        if (layoutChanged)
        {
            ApplyLayout(host, state);
            state.Canvas?.InvalidateMeasure();
            maxH = state.ScrollViewer.ScrollableWidth;
            maxV = state.ScrollViewer.ScrollableHeight;
            newOffsetX = Math.Min(newOffsetX, maxH);
            newOffsetY = Math.Min(newOffsetY, maxV);
            state.PanStart = current;
            state.PanStartOffset = new Point(
                Math.Max(0, Math.Min(newOffsetX, maxH)),
                Math.Max(0, Math.Min(newOffsetY, maxV)));
        }

        var appliedOffsetX = Math.Max(0, Math.Min(newOffsetX, maxH));
        var appliedOffsetY = Math.Max(0, Math.Min(newOffsetY, maxV));

        state.ScrollViewer.ScrollToHorizontalOffset(appliedOffsetX);
        state.ScrollViewer.ScrollToVerticalOffset(appliedOffsetY);
        state.PanStart = current;
        state.PanStartOffset = new Point(appliedOffsetX, appliedOffsetY);
        UpdateVisibleRegion(host, state);
        e.Handled = true;
    }

    private static void ApplyLayout(FrameworkElement host, SurfaceState state)
    {
        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.Canvas is null)
        {
            return;
        }

        // The Jalium NodeEditorDemo model: views are positioned at world + ActualOffset via
        // Canvas.Left/Top (NO RenderTransform), so slot anchors / hit-testing / links all share
        // the same world-coordinate math and can never go stale on pan / auto-grow.
        UpdateGridDecorator(viewModel, state);
        UpdateMinimapOverlay(viewModel, state);
    }

    private static void UpdateVisibleRegion(FrameworkElement host, SurfaceState state)
    {
        if (host.DataContext is not IWorkflowTreeViewModel viewModel || state.ScrollViewer is null)
        {
            return;
        }

        UpdateGridDecorator(viewModel, state);
        UpdateMinimapOverlay(viewModel, state);
        var viewportX = state.ScrollViewer.HorizontalOffset - viewModel.Layout.ActualOffset.Horizontal;
        var viewportY = state.ScrollViewer.VerticalOffset - viewModel.Layout.ActualOffset.Vertical;
        viewModel.GetHelper().Viewport = new Viewport(
            viewportX, viewportY,
            state.ScrollViewer.ViewportWidth,
            state.ScrollViewer.ViewportHeight);
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

        if (source is ScrollBar || ancestors.Any(x => x is ScrollBar))
        {
            return false;
        }

        return source == state.Canvas
            || source == state.ScrollViewer
            || source == state.PointerPressSource
            || source == state.GridDecorator
            || ancestors.Any(x => x == state.Canvas
                || x == state.ScrollViewer
                || x == state.PointerPressSource
                || x == state.GridDecorator);
    }

    private static bool IsWorkflowNodeOrSlotVisual(DependencyObject source)
        => source is FrameworkElement { DataContext: IWorkflowNodeViewModel or IWorkflowSlotViewModel };

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
