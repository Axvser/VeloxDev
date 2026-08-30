using System.ComponentModel;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowSurfaceBehavior
{
    private sealed class SurfaceState
    {
        public ContentView? Host { get; set; }
        public ScrollView? ScrollViewer { get; set; }
        public AbsoluteLayout? Canvas { get; set; }
        public View? GridDecorator { get; set; }
        public View? MinimapOverlay { get; set; }
        public View? PointerPressSource { get; set; }
        public PanGestureRecognizer? PanGesture { get; set; }
        public PinchGestureRecognizer? ZoomGesture { get; set; }
        public double ZoomStartScale { get; set; }
#if WINDOWS
        public Microsoft.UI.Xaml.Input.PointerEventHandler? ZoomWheelHandler { get; set; }
#endif
        public INotifyPropertyChanged? LayoutNotifier { get; set; }
        public PropertyChangedEventHandler? LayoutChangedHandler { get; set; }
        /// <summary>Anchor scroll offset of the current pan gesture — the scroll position the
        /// gesture began at (or the last edge/node-drag re-anchor). Each Running computes the
        /// target ABSOLUTELY as anchor − (Total − anchorTotal), the same math WPF and the minimap
        /// use. No per-delta accumulation: a clamped/rounded ScrollToAsync can't accumulate
        /// bookkeeping drift, which is what jittered during drag and jumped at release.</summary>
        public double PanAccumulatedX { get; set; }
        /// <summary>Anchor scroll offset of the current pan gesture (vertical).</summary>
        public double PanAccumulatedY { get; set; }
        /// <summary>Gesture TotalX at the pan anchor. The pointer's current distance from the
        /// anchor is (TotalX − PanAnchorTotalX); the scroll target is anchor − that distance.</summary>
        public double PanAnchorTotalX { get; set; }
        /// <summary>Gesture TotalY at the pan anchor (vertical).</summary>
        public double PanAnchorTotalY { get; set; }
        public bool HasPendingScrollRestore { get; set; }
        public bool IsRefreshing { get; set; }
        public bool IsVisibleRegionUpdateQueued { get; set; }
        public double PendingViewportX { get; set; }
        public double PendingViewportY { get; set; }

        /// <summary>Cancels the previous in-flight ScrollToAsync on each new pan delta,
        /// preventing stack-up of outdated scroll operations.</summary>
        public CancellationTokenSource? PanCts { get; set; }

        /// <summary>True while a pan gesture is active (Started through Completed/Canceled).
        /// No longer gates OnScrolled or the decorator writers — those are always active now so
        /// grid + content track the native offset together. Kept for the diagnostic trace.</summary>
        public bool PanGestureActive { get; set; }
    }

    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSurfaceBehavior),
        false,
        propertyChanged: OnIsEnabledChanged);

    public static readonly BindableProperty ScrollViewerNameProperty = BindableProperty.CreateAttached(
        "ScrollViewerName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        null);

    public static readonly BindableProperty CanvasNameProperty = BindableProperty.CreateAttached(
        "CanvasName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        null);

    public static readonly BindableProperty GridDecoratorNameProperty = BindableProperty.CreateAttached(
        "GridDecoratorName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        null);

    public static readonly BindableProperty PointerPressSourceNameProperty = BindableProperty.CreateAttached(
        "PointerPressSourceName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        null);

    public static readonly BindableProperty MinimapOverlayNameProperty = BindableProperty.CreateAttached(
        "MinimapOverlayName",
        typeof(string),
        typeof(WorkflowSurfaceBehavior),
        null);

    public static readonly BindableProperty ZoomEnabledProperty = BindableProperty.CreateAttached(
        "ZoomEnabled",
        typeof(bool),
        typeof(WorkflowSurfaceBehavior),
        false,
        propertyChanged: OnZoomEnabledChanged);

    private static readonly BindableProperty StateProperty = BindableProperty.CreateAttached(
        "State",
        typeof(SurfaceState),
        typeof(WorkflowSurfaceBehavior),
        null);

    public static bool GetIsEnabled(BindableObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(BindableObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    public static bool GetZoomEnabled(BindableObject element) => (bool)element.GetValue(ZoomEnabledProperty);
    public static void SetZoomEnabled(BindableObject element, bool value) => element.SetValue(ZoomEnabledProperty, value);
    public static string? GetScrollViewerName(BindableObject element) => (string?)element.GetValue(ScrollViewerNameProperty);
    public static void SetScrollViewerName(BindableObject element, string? value) => element.SetValue(ScrollViewerNameProperty, value);
    public static string? GetCanvasName(BindableObject element) => (string?)element.GetValue(CanvasNameProperty);
    public static void SetCanvasName(BindableObject element, string? value) => element.SetValue(CanvasNameProperty, value);
    public static string? GetGridDecoratorName(BindableObject element) => (string?)element.GetValue(GridDecoratorNameProperty);
    public static void SetGridDecoratorName(BindableObject element, string? value) => element.SetValue(GridDecoratorNameProperty, value);
    public static string? GetPointerPressSourceName(BindableObject element) => (string?)element.GetValue(PointerPressSourceNameProperty);
    public static void SetPointerPressSourceName(BindableObject element, string? value) => element.SetValue(PointerPressSourceNameProperty, value);
    public static string? GetMinimapOverlayName(BindableObject element) => (string?)element.GetValue(MinimapOverlayNameProperty);
    public static void SetMinimapOverlayName(BindableObject element, string? value) => element.SetValue(MinimapOverlayNameProperty, value);

    public static void Refresh(ContentView host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (!GetIsEnabled(host))
        {
            return;
        }

        var state = (SurfaceState?)host.GetValue(StateProperty);
        if (state is null)
        {
            return;
        }

        // Re-entrancy guard: prevent cascading Refresh cycles when canvas expansion
        // during ApplyLayout triggers Scrolled/SizeChanged which call Refresh again.
        // Without this guard, each canvas expansion cascades 2-3 Refresh calls,
        // compounding into a positive-feedback slowdown spiral.
        if (state.IsRefreshing)
        {
            return;
        }

        state.IsRefreshing = true;
        try
        {
            ApplyLayout(host, state);
            UpdateVisibleRegion(host, state);
            ApplyPendingScrollRestore(host, state);
        }
        finally
        {
            state.IsRefreshing = false;
        }
    }

    public static void RequestViewportRestore(ContentView host, double viewportX, double viewportY)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (!GetIsEnabled(host))
        {
            return;
        }

        var state = (SurfaceState?)host.GetValue(StateProperty);
        if (state is null)
        {
            return;
        }

        state.PendingViewportX = viewportX;
        state.PendingViewportY = viewportY;
        state.HasPendingScrollRestore = true;
        Refresh(host);
    }

    internal static bool TryGetViewport(ContentView host, out double viewportX, out double viewportY)
    {
        viewportX = 0;
        viewportY = 0;

        if (!GetIsEnabled(host)
            || host.GetValue(StateProperty) is not SurfaceState state
            || state.ScrollViewer is null
            || ResolveTreeViewModel(host, state) is not { } viewModel)
        {
            return false;
        }

        viewportX = WorkflowSurfaceMath.ToWorld(state.ScrollViewer.ScrollX, viewModel.Layout.ActualOffset.Horizontal);
        viewportY = WorkflowSurfaceMath.ToWorld(state.ScrollViewer.ScrollY, viewModel.Layout.ActualOffset.Vertical);
        return true;
    }

    private static void OnIsEnabledChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not ContentView control)
        {
            return;
        }

        if (newValue is true)
        {
            Attach(control);
            return;
        }

        Detach(control);
    }

    private static void Attach(ContentView control)
    {
        Detach(control);

        var state = new SurfaceState
        {
            Host = control,
        };
        control.SetValue(StateProperty, state);
        control.Loaded += OnLoaded;
        control.Unloaded += OnUnloaded;
        control.BindingContextChanged += OnBindingContextChanged;
        control.SizeChanged += OnHostSizeChanged;
        ResolveNamedControls(control, state);
        UpdateLayoutSubscription(control, state);
        Refresh(control);
    }

    private static void Detach(ContentView control)
    {
        control.Loaded -= OnLoaded;
        control.Unloaded -= OnUnloaded;
        control.BindingContextChanged -= OnBindingContextChanged;
        control.SizeChanged -= OnHostSizeChanged;

        if (control.GetValue(StateProperty) is SurfaceState state)
        {
            UnsubscribeResolvedControls(state);
            UnsubscribeLayout(state);
            state.Host = null;
        }

        control.ClearValue(StateProperty);
    }

    private static void OnLoaded(object? sender, EventArgs e)
    {
        if (sender is ContentView control && control.GetValue(StateProperty) is SurfaceState state)
        {
            ResolveNamedControls(control, state);
            UpdateLayoutSubscription(control, state);
            Refresh(control);
        }
    }

    private static void OnUnloaded(object? sender, EventArgs e)
    {
        if (sender is ContentView control && control.GetValue(StateProperty) is SurfaceState state)
        {
            UnsubscribeLayout(state);
        }
    }

    private static void OnBindingContextChanged(object? sender, EventArgs e)
    {
        if (sender is not ContentView control || control.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        UpdateLayoutSubscription(control, state);
        Refresh(control);
    }

    private static void OnHostSizeChanged(object? sender, EventArgs e)
    {
        if (sender is ContentView control)
        {
            Refresh(control);
        }
    }

    private static void ResolveNamedControls(ContentView control, SurfaceState state)
    {
        UnsubscribeResolvedControls(state);

        var scrollViewerName = GetScrollViewerName(control);
        var canvasName = GetCanvasName(control);
        var gridDecoratorName = GetGridDecoratorName(control);
        var pointerPressSourceName = GetPointerPressSourceName(control);

        if (!string.IsNullOrWhiteSpace(scrollViewerName))
        {
            state.ScrollViewer = control.FindByName<ScrollView>(scrollViewerName);
        }

        if (!string.IsNullOrWhiteSpace(canvasName))
        {
            state.Canvas = control.FindByName<AbsoluteLayout>(canvasName);
            if (state.Canvas is not null)
            {
                state.Canvas.ChildAdded += OnCanvasChildAdded;
                state.Canvas.ChildRemoved += OnCanvasChildRemoved;
            }
        }

        if (!string.IsNullOrWhiteSpace(gridDecoratorName))
        {
            state.GridDecorator = control.FindByName<View>(gridDecoratorName);
        }

        var minimapOverlayName = GetMinimapOverlayName(control);
        if (!string.IsNullOrWhiteSpace(minimapOverlayName))
        {
            state.MinimapOverlay = control.FindByName<View>(minimapOverlayName);
        }

        if (!string.IsNullOrWhiteSpace(pointerPressSourceName))
        {
            state.PointerPressSource = control.FindByName<View>(pointerPressSourceName);
            if (state.PointerPressSource is not null)
            {
                state.PanGesture = new PanGestureRecognizer();
                state.PanGesture.PanUpdated += OnPanUpdated;
                state.PointerPressSource.GestureRecognizers.Add(state.PanGesture);
            }
        }

        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.Scrolled += OnScrolled;
            state.ScrollViewer.SizeChanged += OnScrollViewerSizeChanged;
            // Configure the native ScrollViewer once its platform view exists (the handler can
            // be created after this attachment runs). Re-runs if the handler is re-created.
            state.ScrollViewer.HandlerChanged += OnScrollViewerHandlerChanged;
            OnScrollViewerHandlerChanged(state.ScrollViewer, EventArgs.Empty);
        }

        if (GetZoomEnabled(control))
        {
            HookZoom(control, state);
        }
    }

    private static void UnsubscribeResolvedControls(SurfaceState state)
    {
        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.Scrolled -= OnScrolled;
            state.ScrollViewer.SizeChanged -= OnScrollViewerSizeChanged;
            state.ScrollViewer.HandlerChanged -= OnScrollViewerHandlerChanged;
        }

        if (state.Canvas is not null)
        {
            state.Canvas.ChildAdded -= OnCanvasChildAdded;
            state.Canvas.ChildRemoved -= OnCanvasChildRemoved;
        }

        if (state.PointerPressSource is not null && state.PanGesture is not null)
        {
            state.PanGesture.PanUpdated -= OnPanUpdated;
            state.PointerPressSource.GestureRecognizers.Remove(state.PanGesture);
        }

        UnhookZoom(state);
        state.PanCts?.Cancel();
        state.PanCts = null;
        state.ScrollViewer = null;
        state.Canvas = null;
        state.GridDecorator = null;
        state.MinimapOverlay = null;
        state.PointerPressSource = null;
        state.PanGesture = null;
    }

    private static void OnZoomEnabledChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not ContentView control || control.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        if (Equals(newValue, true))
        {
            HookZoom(control, state);
        }
        else
        {
            UnhookZoom(state);
        }
    }

    // MAUI is cross-platform: touch zooms via a pinch gesture (works everywhere); on Windows,
    // Ctrl + mouse-wheel is also accepted. Both write Layout.Scale (Core collapses the nodes).
    private static void HookZoom(ContentView control, SurfaceState state)
    {
        if (state.PointerPressSource is not null && state.ZoomGesture is null)
        {
            state.ZoomGesture = new PinchGestureRecognizer();
            state.ZoomGesture.PinchUpdated += OnPinchUpdated;
            state.PointerPressSource.GestureRecognizers.Add(state.ZoomGesture);
        }

#if WINDOWS
        if (state.ZoomWheelHandler is null)
        {
            // Capture the MAUI host in the closure: the platform element has no DataContext, but the
            // MAUI ContentView's BindingContext is the workflow tree.
            var captured = control;
            state.ZoomWheelHandler = (s, ev) => OnZoomWheelChanged(s, ev, captured);
            if (control.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement el)
            {
                el.AddHandler(Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent, state.ZoomWheelHandler, true);
            }
            else
            {
                control.HandlerChanged += OnZoomHandlerChanged;
            }
        }
#endif
    }

    private static void UnhookZoom(SurfaceState state)
    {
        if (state.PointerPressSource is not null && state.ZoomGesture is not null)
        {
            state.ZoomGesture.PinchUpdated -= OnPinchUpdated;
            state.PointerPressSource.GestureRecognizers.Remove(state.ZoomGesture);
        }

        state.ZoomGesture = null;

#if WINDOWS
        if (state.Host is not null && state.ZoomWheelHandler is not null)
        {
            state.Host.HandlerChanged -= OnZoomHandlerChanged;
            if (state.Host.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement el)
            {
                el.RemoveHandler(Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent, state.ZoomWheelHandler);
            }
        }

        state.ZoomWheelHandler = null;
#endif
    }

#if WINDOWS
    private static void OnZoomHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is not ContentView control || control.GetValue(StateProperty) is not SurfaceState state || state.ZoomWheelHandler is null)
        {
            return;
        }

        if (control.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement el)
        {
            el.AddHandler(Microsoft.UI.Xaml.UIElement.PointerWheelChangedEvent, state.ZoomWheelHandler, true);
        }
    }

    private static void OnZoomWheelChanged(object? sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e, ContentView control)
    {
        if (control.GetValue(StateProperty) is not SurfaceState state
            || control.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement source
            || ResolveTreeViewModel(control, state) is not { } viewModel
            || !e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
        {
            return;
        }

        var delta = e.GetCurrentPoint(source).Properties.MouseWheelDelta;
        var factor = delta > 0 ? 1.1 : 1 / 1.1;
        var next = Math.Max(0.1, Math.Min(10, viewModel.Layout.Scale.Horizontal * factor));
        viewModel.Layout.Scale = new Scale(next, next);
        e.Handled = true;
    }
#endif

    private static void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (sender is not BindableObject bindable)
        {
            return;
        }

        var host = FindAncestorContentView(bindable);
        if (host is null || host.GetValue(StateProperty) is not SurfaceState state)
        {
            return;
        }

        var tree = ResolveTreeViewModel(host, state);
        if (tree is null)
        {
            return;
        }

        switch (e.Status)
        {
            case GestureStatus.Started:
                state.ZoomStartScale = tree.Layout.Scale.Horizontal;
                break;
            case GestureStatus.Running:
                var factor = Math.Max(0.1, Math.Min(10, state.ZoomStartScale * e.Scale));
                tree.Layout.Scale = new Scale(factor, factor);
                break;
        }
    }

    private static void UpdateLayoutSubscription(ContentView control, SurfaceState state)
    {
        UnsubscribeLayout(state);

        var tree = ResolveTreeViewModel(control, state);
        if (tree is null || tree.Layout is not INotifyPropertyChanged notifier)
        {
            return;
        }

        state.LayoutNotifier = notifier;
        state.LayoutChangedHandler = (_, e) =>
        {
            // Only react to OriginSize changes which can happen outside of
            // scroll/pan (e.g. AdaptTo, programmatic resize).  During pan,
            // PositiveOffset/NegativeOffset change on every frame but those
            // are already handled by ApplyPanAsync calling ApplyLayout +
            // UpdateVisibleRegion directly.  Reacting to them here would
            // triple the work per pan frame (LayoutChangedHandler fires,
            // then OnScrolled fires from ScrollToAsync), causing cascading
            // slowdown.
            // ViewportOffset is also filtered to avoid the circular update
            // where ApplyVisibleRegion sets ViewportOffset which would
            // trigger another Refresh.
            if (e.PropertyName is nameof(CanvasLayout.OriginSize))
            {
                Refresh(control);
            }
        };
        notifier.PropertyChanged += state.LayoutChangedHandler;
    }

    private static void UnsubscribeLayout(SurfaceState state)
    {
        if (state.LayoutNotifier is not null && state.LayoutChangedHandler is not null)
        {
            state.LayoutNotifier.PropertyChanged -= state.LayoutChangedHandler;
            state.LayoutNotifier = null;
            state.LayoutChangedHandler = null;
        }
    }

    private static void OnCanvasChildAdded(object? sender, ElementEventArgs e)
    {
        if (sender is not AbsoluteLayout canvas)
        {
            return;
        }

        if (e.Element is ContentView child)
        {
            WorkflowSlotLayoutBehavior.Refresh(child);
        }

        var host = FindAncestorContentView(canvas);
        if (host is not null)
        {
            MainThread.BeginInvokeOnMainThread(() => Refresh(host));
        }
    }

    private static void OnCanvasChildRemoved(object? sender, ElementEventArgs e)
    {
        if (sender is AbsoluteLayout canvas && FindAncestorContentView(canvas) is { } host)
        {
            MainThread.BeginInvokeOnMainThread(() => Refresh(host));
        }
    }

    private static ContentView? FindAncestorContentView(BindableObject bindable)
    {
        Element? current = bindable as Element;
        while (current is not null)
        {
            if (current is ContentView contentView && GetIsEnabled(contentView))
            {
                return contentView;
            }

            current = current.Parent;
        }

        return null;
    }

    private static void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        if (sender is not ScrollView viewer)
        {
            return;
        }

        var host = FindAncestorContentView(viewer);
        if (host is null)
        {
            return;
        }

        var state = (SurfaceState?)host.GetValue(StateProperty);
        if (state is null)
        {
            return;
        }

        // CRITICAL: do NOT suppress Scrolled during a pan. MAUI fires this on every native
        // ViewChanged (intermediate + final), and ScrollX/ScrollY are the native offsets at
        // that instant — the ONLY trustworthy position. ScrollToAsync is a fire-and-forget
        // ChangeView that can land short of the request (it never guarantees the target is hit),
        // and on Windows the native ScrollViewer's manipulation can move the content on its
        // own. Suppressing Scrolled froze the decorators at the requested target during the
        // drag, so when the native settled elsewhere after release the decorators snapped —
        // the release jump. Letting every scroll through (the minimap's path) keeps grid +
        // content glued at all times.
        Refresh(host);
    }

    private static void OnScrollViewerSizeChanged(object? sender, EventArgs e)
    {
        if (sender is ScrollView viewer)
        {
            var host = FindAncestorContentView(viewer);
            if (host is not null)
            {
                Refresh(host);
            }
        }
    }

    /// <summary>
    /// On Windows the native ScrollViewer must be a PASSIVE receiver of ChangeView only. MAUI's
    /// pan recognizer drives the surface through native manipulation events (ManipulationMode 35
    /// on the pointer-press source), and the nested ScrollViewer — a manipulation-capable control
    /// with the default <c>ManipulationMode.System</c> — would otherwise claim the manipulation
    /// as its container and scroll the content itself, with inertia. It then fights our
    /// programmatic ChangeView calls, and on pointer release its manipulation completes and
    /// applies its OWN accumulated offset — the post-release content jump we chased during
    /// the release-pan investigation.
    ///
    /// We demote it to a passive MANUAL container instead: <c>ManipulationMode</c> to the same
    /// TranslateX|TranslateY|Scale value MAUI uses on gesture containers. A non-<c>System</c>
    /// mode stops the ScrollViewer from claiming the gesture for its own scrolling, so ChangeView
    /// is the sole scroll driver (the same model the WinUI adapter uses). The manipulation still
    /// initiates on this control (it is the nearest non-<c>None</c> ancestor of the canvas) and
    /// bubbles to the parent's handlers, which is how the pan keeps working.
    ///
    /// IMPORTANT: do NOT use <c>ManipulationModes.None</c> here. None disables manipulation for
    /// the element AND its entire subtree, so no manipulation ever initiates inside the canvas —
    /// the pan stops working entirely (observed regression).
    /// </summary>
    private static void OnScrollViewerHandlerChanged(object? sender, EventArgs e)
    {
        if (sender is not ScrollView viewer)
        {
            return;
        }
#if WINDOWS
        if (viewer.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ScrollViewer sv)
        {
            sv.IsScrollInertiaEnabled = false;
            sv.ManipulationMode = Microsoft.UI.Xaml.Input.ManipulationModes.TranslateX
                | Microsoft.UI.Xaml.Input.ManipulationModes.TranslateY
                | Microsoft.UI.Xaml.Input.ManipulationModes.Scale;
        }
#endif
    }

    private static void ApplyLayout(ContentView host, SurfaceState state)
    {
        var viewModel = ResolveTreeViewModel(host, state);
        if (viewModel is null || state.Canvas is null)
        {
            return;
        }

        var actualOffset = viewModel.Layout.ActualOffset;
        var actualSize = viewModel.Layout.ActualSize;

        state.Canvas.Margin = new Thickness(0);
        // Canvas-level TranslationX shifts ALL children simultaneously, keeping
        // nodes, links, and grid in perfect sync on every frame.
        // Per-child TranslationX (tried previously) causes frame-level desync
        // between decorator updates and child transforms.
        // WidthRequest is set in the SAME call from model data (not XAML binding
        // which adds async lag), avoiding the clipping that motivated the per-child
        // experiment.
        state.Canvas.TranslationX = actualOffset.Horizontal;
        state.Canvas.TranslationY = actualOffset.Vertical;

        if (state.Canvas.WidthRequest < actualSize.Width ||
            double.IsNaN(state.Canvas.WidthRequest))
            state.Canvas.WidthRequest = Math.Max(1, actualSize.Width);
        if (state.Canvas.HeightRequest < actualSize.Height ||
            double.IsNaN(state.Canvas.HeightRequest))
            state.Canvas.HeightRequest = Math.Max(1, actualSize.Height);

        // Decorator/minimap offsets are viewport data. Written from ScrollX (the native offset
        // as of the last ViewChanged) on EVERY Refresh — including during a pan — so grid +
        // content never diverge. The decorators coalesce their redraws (ScheduleInvalidate),
        // so the second write from ApplyVisibleRegion later in this Refresh is free.
        if (state.ScrollViewer is not null)
        {
            UpdateGridDecorator(viewModel, state, state.ScrollViewer.ScrollX, state.ScrollViewer.ScrollY);
            UpdateMinimapOverlay(viewModel, state, state.ScrollViewer.ScrollX, state.ScrollViewer.ScrollY);
        }
    }

    private static async void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        try
        {
            if (sender is not BindableObject bindable)
            {
                return;
            }

            var host = FindAncestorContentView(bindable);
            if (host is null || host.GetValue(StateProperty) is not SurfaceState state || state.ScrollViewer is null)
            {
                return;
            }

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    // Anchor the pan at the current scroll + pointer position. Each Running
                    // computes the target absolutely from this anchor (WPF/minimap style)
                    // instead of accumulating per-delta differences, which drift whenever a
                    // clamped ScrollToAsync lands off the requested offset.
                    state.PanAccumulatedX = state.ScrollViewer.ScrollX;
                    state.PanAccumulatedY = state.ScrollViewer.ScrollY;
                    state.PanAnchorTotalX = e.TotalX;
                    state.PanAnchorTotalY = e.TotalY;
                    state.PanGestureActive = true;
                    break;
                case GestureStatus.Running:
                    if (WorkflowNodeDragBehavior.IsDraggingNode || WorkflowSlotConnectionBehavior.IsDraggingConnection)
                    {
                        // Keep the anchor glued to the current scroll + pointer while a
                        // node/connection drag suppresses canvas panning, so that when the
                        // drag ends mid-gesture the resumed pan continues from the current
                        // position instead of jumping by the node-drag pointer distance.
                        state.PanAccumulatedX = state.ScrollViewer.ScrollX;
                        state.PanAccumulatedY = state.ScrollViewer.ScrollY;
                        state.PanAnchorTotalX = e.TotalX;
                        state.PanAnchorTotalY = e.TotalY;
                        break;
                    }

                    await ApplyPanAsync(host, state, e);
                    break;
                case GestureStatus.Canceled:
                case GestureStatus.Completed:
                    {
                        state.PanCts?.Cancel();
                        state.PanCts = null;
                        state.PanGestureActive = false;
                        state.PanAccumulatedX = state.ScrollViewer.ScrollX;
                        state.PanAccumulatedY = state.ScrollViewer.ScrollY;
                        // No forced finalize: the last ChangeView's landing fires Scrolled ->
                        // OnScrolled -> Refresh, which writes the decorators from the settled
                        // native offset. Dropping the flag is safe now that OnScrolled is the
                        // single decorator writer and reads ScrollX (native truth) directly.
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WorkflowSurfaceBehavior] Pan error: {ex.Message}");
        }
    }

    private static async Task ApplyPanAsync(ContentView host, SurfaceState state, PanUpdatedEventArgs e)
    {
        var viewModel = ResolveTreeViewModel(host, state);
        if (viewModel is null || state.ScrollViewer is null)
        {
            return;
        }

        // Cancel any previous in-flight ScrollToAsync to prevent cascading.
        state.PanCts?.Cancel();
        state.PanCts = new CancellationTokenSource();
        var ct = state.PanCts.Token;

        // Absolute target from the pan anchor — the same math WPF uses (startOffset + pointer
        // movement since the anchor). Reading the actual ScrollX each frame is what caused the
        // flash-back; accumulating a per-delta offset is what jittered (a clamped ScrollToAsync
        // lets the bookkeeping drift from the real position, so the content sticks then jumps).
        // The anchor only moves in Started, node-drag suppression, and the edge re-anchor below,
        // so it never accumulates error.
        var desiredX = state.PanAccumulatedX - (e.TotalX - state.PanAnchorTotalX);
        var desiredY = state.PanAccumulatedY - (e.TotalY - state.PanAnchorTotalY);
        var maxH = GetHorizontalScrollMaximum(state);
        var maxV = GetVerticalScrollMaximum(state);
        // layoutChanged = the desired offset overshoots [0, max] on either axis, which is
        // exactly when ClampScrollOffset (below) expands the canvas.  Kept adapter-specific
        // so the max-recompute flow below stays unchanged.
        var layoutChanged = desiredX < 0 || desiredX > maxH || desiredY < 0 || desiredY > maxV;

        // Expand canvas when pan reaches the edge. Expansion IS the correct behavior
        // (matching WPF). The original crash was caused by a cascade:
        // expansion → Refresh → more expansion. The IsRefreshing guard in Refresh()
        // breaks this cycle.

        // ClampScrollOffset writes the overshoot into NegativeOffset (before origin) or
        // PositiveOffset (past the content edge) and returns the clamped offset to apply.
        // threshold 0 = always expand, matching the previous inline branches.
        var newOffsetX = WorkflowSurfaceMath.ClampScrollOffset(desiredX, maxH, viewModel.Layout, horizontal: true, threshold: 0);
        var newOffsetY = WorkflowSurfaceMath.ClampScrollOffset(desiredY, maxV, viewModel.Layout, horizontal: false, threshold: 0);

        if (layoutChanged)
        {
            ApplyLayout(host, state);
            // Recompute max from model — canvas size was just updated via ApplyLayout
            // but MAUI layout is async so ScrollViewer.ContentSize is stale.
            maxH = GetHorizontalScrollMaximum(state);
            maxV = GetVerticalScrollMaximum(state);
        }

        // Guard against NaN from ScrollViewer.Width/Height during async layout.
        // Math.Max(0, NaN) = NaN, which would crash ScrollToAsync.
        if (!double.IsFinite(maxH)) maxH = 0;
        if (!double.IsFinite(maxV)) maxV = 0;

        // Bound the applied target by the NATIVE ScrollViewer extent, not just the model.
        // On overscroll the model's ActualSize (and canvas WidthRequest) grows
        // SYNCHRONOUSLY, but the native content re-measures ASYNCHRONOUSLY, so during a
        // fast drag the native extent can lag the model by thousands of pixels. ChangeView
        // clamps to the native extent, so a model-based target lands short of the native edge.
        // Re-anchoring at the model edge then sets the bookkeeping AHEAD of the native:
        // the content pins at the stale native edge and, when the native finally re-measures,
        // jumps forward in one frame to catch the anchor. Clamping the applied target to the
        // live native extent makes every ChangeView land exactly, so the content eases with
        // the re-measure instead of jumping (and never over-shoots on release).
        var appliedOffsetX = Math.Max(0, Math.Min(newOffsetX, GetNativeScrollMaximum(state, horizontal: true)));
        var appliedOffsetY = Math.Max(0, Math.Min(newOffsetY, GetNativeScrollMaximum(state, horizontal: false)));

        if (layoutChanged)
        {
            // Re-anchor at the APPLIED (native-bounded) offset — never the model edge — so
            // the bookkeeping stays exactly where the content will actually land this frame.
            state.PanAccumulatedX = appliedOffsetX;
            state.PanAccumulatedY = appliedOffsetY;
            state.PanAnchorTotalX = e.TotalX;
            state.PanAnchorTotalY = e.TotalY;
        }

        // The decorators are NOT written from the requested target: ChangeView is fire-and-forget
        // and can land short of the request (the native manipulation fights it on Windows), so
        // writing the requested target would desync the grid from the content. The native
        // ViewChanged -> Scrolled -> OnScrolled -> Refresh path writes the decorators from the
        // ACTUAL landed position on every move — the same single-writer path the smooth minimap uses.
        try
        {
            await state.ScrollViewer.ScrollToAsync(appliedOffsetX, appliedOffsetY, false);
        }
        catch (OperationCanceledException)
        {
            // Previous scroll was superseded by a newer pan delta — expected.
            return;
        }

        if (!ct.IsCancellationRequested)
        {
            UpdateVisibleRegion(host, state);
        }
    }



    private static double GetHorizontalScrollMaximum(SurfaceState state)
    {
        // Compute max scroll from the layout model.  Canvas size is driven
        // by ViewModel binding (Layout.ActualSize).  The ScrollView content
        // is the AbsoluteLayout which follows ActualSize from the binding,
        // so ActualSize - viewportWidth gives the correct scroll extent.
        var viewModel = ResolveTreeViewModel(state.Host!, state);
        if (viewModel is null || state.ScrollViewer is null) return 0;
        var w = viewModel.Layout.ActualSize.Width - state.ScrollViewer.Width;
        return double.IsNaN(w) || w < 0 ? 0 : w;
    }

    private static double GetVerticalScrollMaximum(SurfaceState state)
    {
        var viewModel = ResolveTreeViewModel(state.Host!, state);
        if (viewModel is null || state.ScrollViewer is null) return 0;
        var h = viewModel.Layout.ActualSize.Height - state.ScrollViewer.Height;
        return double.IsNaN(h) || h < 0 ? 0 : h;
    }

    /// <summary>
    /// The scroll extent the native ScrollViewer will actually accept RIGHT NOW:
    /// <c>min(model extent, native Extent − viewport)</c>. On overscroll the model's
    /// <see cref="CanvasLayout.ActualSize"/> (and the canvas WidthRequest/HeightRequest set by
    /// <see cref="ApplyLayout"/>) grows synchronously while the native content re-measures
    /// asynchronously, so the native extent can lag the model by a lot during a fast drag.
    /// ChangeView clamps to the native extent, so this is the true ceiling for any applied
    /// target — a model-based ceiling lets the content pin at the stale native edge and then
    /// jump forward when the native catches up. Falls back to the model extent on non-Windows
    /// platforms (no ChangeView clamp desync there).
    /// </summary>
    private static double GetNativeScrollMaximum(SurfaceState state, bool horizontal)
    {
        var modelMax = horizontal ? GetHorizontalScrollMaximum(state) : GetVerticalScrollMaximum(state);
#if WINDOWS
        if (state.ScrollViewer?.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ScrollViewer sv)
        {
            var extent = horizontal ? sv.ExtentWidth : sv.ExtentHeight;
            var viewport = horizontal ? sv.ViewportWidth : sv.ViewportHeight;
            if (double.IsFinite(extent) && double.IsFinite(viewport) && extent > 0 && viewport > 0)
            {
                var nativeMax = extent - viewport;
                if (double.IsFinite(nativeMax) && nativeMax >= 0)
                {
                    return Math.Min(modelMax, nativeMax);
                }
            }
        }
#endif
        return modelMax;
    }

    private static IWorkflowTreeViewModel? ResolveTreeViewModel(ContentView host, SurfaceState state)
        => host.BindingContext as IWorkflowTreeViewModel
            ?? state.Canvas?.BindingContext as IWorkflowTreeViewModel
            ?? state.ScrollViewer?.BindingContext as IWorkflowTreeViewModel
            ?? state.GridDecorator?.BindingContext as IWorkflowTreeViewModel
            ?? state.PointerPressSource?.BindingContext as IWorkflowTreeViewModel;

    private static void UpdateVisibleRegion(ContentView host, SurfaceState state)
    {
        if (state.IsVisibleRegionUpdateQueued)
        {
            return;
        }

        state.IsVisibleRegionUpdateQueued = true;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!GetIsEnabled(host)
                || host.GetValue(StateProperty) is not SurfaceState currentState
                || !ReferenceEquals(currentState, state))
            {
                return;
            }

            state.IsVisibleRegionUpdateQueued = false;
            ApplyVisibleRegion(host, state);
        });
    }

    private static void ApplyVisibleRegion(ContentView host, SurfaceState state)
    {
        var viewModel = ResolveTreeViewModel(host, state);
        if (viewModel is null || state.ScrollViewer is null)
        {
            return;
        }

        // CRITICAL: After ScrollToAsync completes, ScrollViewer dimensions and
        // scroll position can still be NaN/zero during MAUI's async layout pass.
        // NaN propagates through Viewport → Virtualize → spatial index, causing
        // all VisibleItems to be cleared (links permanently disappear).
        // NaN <= 0 returns false in C#, so Virtualize's guard does NOT catch this.
        // CRITICAL: always write from ScrollX — the native offset as of the last ViewChanged,
        // i.e. where the content ACTUALLY is. The requested target can differ from ScrollX
        // whenever a ChangeView lands short, and writing the requested target here is exactly
        // what made the grid snap back to the true position after release.
        var scrollX = state.ScrollViewer.ScrollX;
        var scrollY = state.ScrollViewer.ScrollY;
        var svW = state.ScrollViewer.Width;
        var svH = state.ScrollViewer.Height;

        if (double.IsNaN(scrollX) || double.IsNaN(scrollY) ||
            double.IsNaN(svW) || double.IsNaN(svH) ||
            svW <= 0 || svH <= 0)
        {
            return;
        }

        UpdateGridDecorator(viewModel, state, scrollX, scrollY);
        UpdateMinimapOverlay(viewModel, state, scrollX, scrollY);

        var viewportX = WorkflowSurfaceMath.ToWorld(scrollX, viewModel.Layout.ActualOffset.Horizontal);
        var viewportY = WorkflowSurfaceMath.ToWorld(scrollY, viewModel.Layout.ActualOffset.Vertical);
        viewModel.GetHelper().Viewport = new Viewport(
            double.IsNaN(viewportX) ? 0 : viewportX,
            double.IsNaN(viewportY) ? 0 : viewportY,
            svW, svH);

        // Persist the viewport position so it survives serialization round-trip.
        viewModel.Layout.ViewportOffset = new Offset(
            double.IsNaN(scrollX) ? 0 : scrollX,
            double.IsNaN(scrollY) ? 0 : scrollY);
    }

    private static void UpdateGridDecorator(IWorkflowTreeViewModel viewModel, SurfaceState state, double scrollX, double scrollY)
    {
        if (state.GridDecorator is not IWorkflowGridDecorator decorator)
        {
            return;
        }

        decorator.ScrollOffsetX = double.IsNaN(scrollX) ? 0 : scrollX;
        decorator.ScrollOffsetY = double.IsNaN(scrollY) ? 0 : scrollY;
        decorator.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
        decorator.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;
    }

    private static void UpdateMinimapOverlay(IWorkflowTreeViewModel viewModel, SurfaceState state, double scrollX, double scrollY)
    {
        if (state.MinimapOverlay is not IWorkflowMinimapOverlay minimap || state.ScrollViewer is null)
        {
            return;
        }

        minimap.ScrollOffsetX = double.IsNaN(scrollX) ? 0 : scrollX;
        minimap.ScrollOffsetY = double.IsNaN(scrollY) ? 0 : scrollY;
        minimap.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
        minimap.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;
        minimap.ViewportWidth = double.IsNaN(state.ScrollViewer.Width) ? 1 : state.ScrollViewer.Width;
        minimap.ViewportHeight = double.IsNaN(state.ScrollViewer.Height) ? 1 : state.ScrollViewer.Height;
        minimap.WorkflowTree = viewModel;
    }

    private static void ApplyPendingScrollRestore(ContentView host, SurfaceState state)
    {
        if (!state.HasPendingScrollRestore || state.ScrollViewer is null)
        {
            return;
        }

        state.HasPendingScrollRestore = false;

        // Dispatch async restoration via IDispatcher to avoid async void.
        // The Task is fire-and-forget but will not silently swallow exceptions.
        _ = host.Dispatcher.DispatchAsync(() => ApplyPendingScrollRestoreCore(host, state));
    }

    private static async Task ApplyPendingScrollRestoreCore(ContentView host, SurfaceState state)
    {
        try
        {
            if (state.ScrollViewer is null)
            {
                return;
            }

            // Yield once so that any pending layout pass (from ApplyLayout called
            // before this method) settles, ensuring ActualOffset is up-to-date.
            await Task.Yield();

            var viewModel = ResolveTreeViewModel(host, state);
            if (viewModel is null)
            {
                return;
            }

            // ToScreen: screen = world + ActualOffset (the pending viewport is in world space).
            var target = WorkflowSurfaceMath.ToScreen(state.PendingViewportX, state.PendingViewportY, viewModel.Layout);
            var targetX = Math.Max(0, Math.Min(target.Horizontal, GetHorizontalScrollMaximum(state)));
            var targetY = Math.Max(0, Math.Min(target.Vertical, GetVerticalScrollMaximum(state)));

            if (Math.Abs(state.ScrollViewer.ScrollX - targetX) > 0.5
                || Math.Abs(state.ScrollViewer.ScrollY - targetY) > 0.5)
            {
                await state.ScrollViewer.ScrollToAsync(targetX, targetY, false);
            }

            UpdateVisibleRegion(host, state);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[WorkflowSurfaceBehavior] ScrollRestore error: {ex.Message}");
        }
    }
}
