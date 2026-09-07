using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;

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

    public static readonly DependencyProperty ZoomEnabledProperty = DependencyProperty.RegisterAttached(
        "ZoomEnabled",
        typeof(bool),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(false, OnZoomEnabledChanged));

    public static readonly DependencyProperty ConsumeCanvasScrollProperty = DependencyProperty.RegisterAttached(
        "ConsumeCanvasScroll",
        typeof(bool),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(false));

    public static readonly DependencyProperty KeyDownProperty = DependencyProperty.RegisterAttached(
        "KeyDown",
        typeof(KeyEventHandler),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty KeyUpProperty = DependencyProperty.RegisterAttached(
        "KeyUp",
        typeof(KeyEventHandler),
        typeof(WorkflowSurfaceBehavior),
        new PropertyMetadata(null));

    public static readonly DependencyProperty MouseWheelProperty = DependencyProperty.RegisterAttached(
        "MouseWheel",
        typeof(MouseWheelEventHandler),
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

    public static bool GetZoomEnabled(DependencyObject element) => (bool)element.GetValue(ZoomEnabledProperty);
    public static void SetZoomEnabled(DependencyObject element, bool value) => element.SetValue(ZoomEnabledProperty, value);

    public static bool GetConsumeCanvasScroll(DependencyObject element) => (bool)element.GetValue(ConsumeCanvasScrollProperty);
    public static void SetConsumeCanvasScroll(DependencyObject element, bool value) => element.SetValue(ConsumeCanvasScrollProperty, value);

    public static KeyEventHandler? GetKeyDown(DependencyObject element) => (KeyEventHandler?)element.GetValue(KeyDownProperty);
    public static void SetKeyDown(DependencyObject element, KeyEventHandler? value) => element.SetValue(KeyDownProperty, value);
    public static KeyEventHandler? GetKeyUp(DependencyObject element) => (KeyEventHandler?)element.GetValue(KeyUpProperty);
    public static void SetKeyUp(DependencyObject element, KeyEventHandler? value) => element.SetValue(KeyUpProperty, value);
    public static MouseWheelEventHandler? GetMouseWheel(DependencyObject element) => (MouseWheelEventHandler?)element.GetValue(MouseWheelProperty);
    public static void SetMouseWheel(DependencyObject element, MouseWheelEventHandler? value) => element.SetValue(MouseWheelProperty, value);

    /// <summary>Global key-down feed for every workflow surface (local injection = attached KeyDown on the host). Runs before the canvas scroll eating, so injected handlers may set Handled to override.</summary>
    public static event KeyEventHandler? KeyDown;

    /// <summary>Global key-up feed for every workflow surface (local injection = attached KeyUp on the host).</summary>
    public static event KeyEventHandler? KeyUp;

    /// <summary>Global mouse-wheel feed for every workflow surface (local injection = attached MouseWheel on the host).
    /// Runs before the canvas scroll eating / zoom, so injected handlers may implement custom scroll (e.g. Alt→horizontal,
    /// Shift→vertical) and set Handled.</summary>
    public static event MouseWheelEventHandler? MouseWheel;

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
        control.PreviewKeyDown += OnPreviewKeyDown;
        control.PreviewKeyUp += OnPreviewKeyUp;
        control.PreviewMouseWheel += OnPreviewMouseWheel;
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
        control.PreviewKeyDown -= OnPreviewKeyDown;
        control.PreviewKeyUp -= OnPreviewKeyUp;
        control.PreviewMouseWheel -= OnPreviewMouseWheel;

        if (control.GetValue(StateProperty) is SurfaceState state)
        {
            UnsubscribeResolvedControls(state);
        }

        control.ClearValue(StateProperty);
    }

    private static void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (sender is not UserControl control || control.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        // Local (attached KeyDown on the host) + global key injection runs first — a handler may set Handled to take over.
        GetKeyDown(control)?.Invoke(control, e);
        KeyDown?.Invoke(control, e);
        if (e.Handled || !GetConsumeCanvasScroll(control) || state.ScrollViewer is null)
        {
            return;
        }

        if (IsScrollNavigationKey(e.Key) && Keyboard.FocusedElement is DependencyObject focused
            && !IsTextEditing(focused)
            && IsInside(focused, state.ScrollViewer) && NearestScrollViewer(focused) == state.ScrollViewer)
        {
            e.Handled = true;
        }
    }

    private static void OnPreviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is not UserControl control)
        {
            return;
        }

        GetKeyUp(control)?.Invoke(control, e);
        KeyUp?.Invoke(control, e);
    }

    private static void OnPreviewMouseWheel(object? sender, MouseWheelEventArgs e)
    {
        if (sender is not UserControl control || control.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        // Wheel injection (local attached MouseWheel on the host + global) runs first — set Handled to take over,
        // e.g. custom modifier+wheel scroll (Alt→horizontal, Shift→vertical).
        GetMouseWheel(control)?.Invoke(control, e);
        MouseWheel?.Invoke(control, e);
        if (e.Handled)
        {
            return;
        }

        if (GetZoomEnabled(control) && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            return;
        }

        if (!GetConsumeCanvasScroll(control) || state.ScrollViewer is null || e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (IsInside(source, state.ScrollViewer) && NearestScrollViewer(source) == state.ScrollViewer)
        {
            e.Handled = true;
        }
    }

    private static bool IsScrollNavigationKey(Key key)
        => key == Key.Up || key == Key.Down || key == Key.Left || key == Key.Right
           || key == Key.Home || key == Key.End || key == Key.PageUp || key == Key.PageDown;

    private static bool IsTextEditing(DependencyObject element)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is System.Windows.Controls.Primitives.TextBoxBase
                or System.Windows.Controls.Primitives.Popup
                or System.Windows.Controls.ComboBox)
            {
                return true;
            }
        }

        return false;
    }

    private static ScrollViewer? NearestScrollViewer(DependencyObject element)
    {
        if (element is ScrollViewer scrollViewer)
        {
            return scrollViewer;
        }

        return EnumerateVisualAncestors(element).OfType<ScrollViewer>().FirstOrDefault();
    }

    private static bool IsInside(DependencyObject element, DependencyObject root)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current == root)
            {
                return true;
            }
        }

        return false;
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

        if (GetZoomEnabled(control))
        {
            HookZoom(state);
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
            state.ScrollViewer.PreviewMouseWheel -= OnZoomPreviewMouseWheel;
        }

        state.PointerPressSource = null;
        state.ScrollViewer = null;
        state.Canvas = null;
        state.GridDecorator = null;
        state.MinimapOverlay = null;
    }

    private static void OnZoomEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UserControl control || control.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (Equals(e.NewValue, true))
        {
            HookZoom(state);
        }
        else
        {
            UnhookZoom(state);
        }
    }

    private static void HookZoom(SurfaceState state)
    {
        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.PreviewMouseWheel += OnZoomPreviewMouseWheel;
        }
    }

    private static void UnhookZoom(SurfaceState state)
    {
        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.PreviewMouseWheel -= OnZoomPreviewMouseWheel;
        }
    }

    private static void OnZoomPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject source)
        {
            return;
        }

        var host = EnumerateVisualAncestors(source).OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host is null || host.DataContext is not IWorkflowTreeViewModel viewModel)
        {
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
        {
            return;
        }

        // Wheel up (positive delta) zooms in: Scale is a collapse factor, so zoom-in divides it by 1/1.1.
        var factor = e.Delta > 0 ? 1 / 1.1 : 1.1;
        var next = Math.Max(0.1, Math.Min(10, viewModel.Layout.Scale.Horizontal * factor));
        var layout = viewModel.Layout;

        if (layout.ZoomCenter == ZoomCenter.ViewportCenter
            && host.GetValue(StateProperty) is SurfaceState state
            && state.ScrollViewer is { } sv)
        {
            // Capture the world point under the viewport center, collapse the nodes about it, then
            // scroll so that point stays put: nodes near the viewport center remain visible while zooming.
            // The canvas geometry is untouched by the zoom (ActualOffset == NegativeOffset, fixed) — only
            // the scroll moves, so the canvas position never jitters.
            var (wx, wy) = WorkflowSurfaceMath.WorldAtViewportCenter(
                sv.HorizontalOffset, sv.VerticalOffset, sv.ViewportWidth, sv.ViewportHeight, layout);
            layout.CollapsePivot = new Anchor(wx, wy, 0);
            layout.Scale = new Scale(next, next);

            // Deep zoom-in collapses negative-world content past the fixed canvas translate (ActualOffset
            // == NegativeOffset); grow the cover BEFORE ApplyLayout adopts the new offset. PivotCenterScroll
            // below reads the grown offset, so the extra cover is absorbed by the scroll target and the
            // pivot stays centered — no manual delta needed. Positive-only content is a no-op.
            WorkflowSurfaceMath.EnsureNegativeCover(viewModel);

            // Let the ScrollViewer adopt the (possibly auto-extended) extent BEFORE reading the max.
            // Zoom-in below scale 1 grows the canvas, zoom-out may shrink it; the clamp must see the
            // settled extent or the pivot lands off-center and the next tick re-captures the drift.
            ApplyLayout(host, state);
            state.Canvas?.UpdateLayout();
            sv.UpdateLayout();
            host.UpdateLayout();

            var (tx, ty) = WorkflowSurfaceMath.PivotCenterScroll(wx, wy, layout, sv.ViewportWidth, sv.ViewportHeight);
            var maxH = GetHorizontalScrollMaximum(sv);
            var maxV = GetVerticalScrollMaximum(sv);

            // Overscroll-expand the canvas (same mechanism as panning past an edge) so the pivot is
            // always reachable; a plain clamp would push the pivot off-center and drift on each tick.
            var newX = WorkflowSurfaceMath.ClampScrollOffset(tx, maxH, layout, horizontal: true);
            var newY = WorkflowSurfaceMath.ClampScrollOffset(ty, maxV, layout, horizontal: false);
            if (Math.Abs(newX - tx) > double.Epsilon || Math.Abs(newY - ty) > double.Epsilon)
            {
                // Expansion changed the extent; re-apply and re-read the max so tx/ty land inside it.
                ApplyLayout(host, state);
                state.Canvas?.UpdateLayout();
                sv.UpdateLayout();
                host.UpdateLayout();
                maxH = GetHorizontalScrollMaximum(sv);
                maxV = GetVerticalScrollMaximum(sv);
            }

            sv.ScrollToHorizontalOffset(WorkflowSurfaceMath.ClampValue(tx, 0, maxH));
            sv.ScrollToVerticalOffset(WorkflowSurfaceMath.ClampValue(ty, 0, maxV));
        }
        else
        {
            layout.Scale = new Scale(next, next);
            // World-origin zoom: the canvas translate only changes when the layout is re-applied, so if
            // the cover grew, push the new offset through the same layout pass the viewport-center branch
            // does (the trimmed demos are viewport-center, so this path normally stays dormant).
            if (WorkflowSurfaceMath.EnsureNegativeCover(viewModel)
                && host.GetValue(StateProperty) is SurfaceState fallbackState
                && fallbackState.Canvas is { } fallbackCanvas
                && fallbackState.ScrollViewer is { } fallbackViewer)
            {
                ApplyLayout(host, fallbackState);
                fallbackCanvas.UpdateLayout();
                fallbackViewer.UpdateLayout();
                host.UpdateLayout();
            }
        }
        e.Handled = true;
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
        var newOffsetX = WorkflowSurfaceMath.ClampScrollOffset(
            desiredX, maxH, viewModel.Layout, horizontal: true, extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);
        var newOffsetY = WorkflowSurfaceMath.ClampScrollOffset(
            desiredY, maxV, viewModel.Layout, horizontal: false, extendRatio: WorkflowSurfaceMath.DefaultPanExtendRatio);
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

            // Keep the virtualization visible-region correction in sync with the decorator's
            // floating ruler band so nodes beneath it are not culled a ruler-thickness early.
            viewModel.SetVirtualizeInset(left: decorator.RulerBand, top: decorator.RulerBand);
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

        // Links are canvas content, not background — pressing one should never start a pan. This is the intended
        // surface policy (not a workaround): an armed link view (WorkflowLinkBehaviors) receives its own press/key
        // states, and an unarmed link simply does nothing on press. Panning stays available on the blank canvas and
        // via node drag.
        if (IsWorkflowLinkVisual(source) || ancestors.Any(IsWorkflowLinkVisual))
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