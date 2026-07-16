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
        public INotifyPropertyChanged? LayoutNotifier { get; set; }
        public PropertyChangedEventHandler? LayoutChangedHandler { get; set; }
        public double LastPanTotalX { get; set; }
        public double LastPanTotalY { get; set; }
        public bool HasPendingScrollRestore { get; set; }
        public bool IsApplyingPendingScrollRestore { get; set; }
        public bool IsVisibleRegionUpdateQueued { get; set; }
        public double PendingViewportX { get; set; }
        public double PendingViewportY { get; set; }
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

    private static readonly BindableProperty StateProperty = BindableProperty.CreateAttached(
        "State",
        typeof(SurfaceState),
        typeof(WorkflowSurfaceBehavior),
        null);

    public static bool GetIsEnabled(BindableObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(BindableObject element, bool value) => element.SetValue(IsEnabledProperty, value);
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

        ApplyLayout(host, state);
        UpdateVisibleRegion(host, state);
        ApplyPendingScrollRestore(host, state);
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

        viewportX = state.ScrollViewer.ScrollX - viewModel.Layout.ActualOffset.Horizontal;
        viewportY = state.ScrollViewer.ScrollY - viewModel.Layout.ActualOffset.Vertical;
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
        }
    }

    private static void UnsubscribeResolvedControls(SurfaceState state)
    {
        if (state.ScrollViewer is not null)
        {
            state.ScrollViewer.Scrolled -= OnScrolled;
            state.ScrollViewer.SizeChanged -= OnScrollViewerSizeChanged;
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

        state.ScrollViewer = null;
        state.Canvas = null;
        state.GridDecorator = null;
        state.MinimapOverlay = null;
        state.PointerPressSource = null;
        state.PanGesture = null;
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
        state.LayoutChangedHandler = (_, _) => Refresh(control);
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
        if (host is not null)
        {
            Refresh(host);
        }
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
        state.Canvas.TranslationX = actualOffset.Horizontal;
        state.Canvas.TranslationY = actualOffset.Vertical;
        // Use actualSize only — it already includes all offset extents.
        // Adding actualOffset would double-count and cause runaway growth.
        state.Canvas.WidthRequest = Math.Max(1, actualSize.Width);
        state.Canvas.HeightRequest = Math.Max(1, actualSize.Height);

        UpdateGridDecorator(viewModel, state);
        UpdateMinimapOverlay(viewModel, state);
    }

    private static async void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
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
                state.LastPanTotalX = 0;
                state.LastPanTotalY = 0;
                break;
            case GestureStatus.Running:
                if (WorkflowNodeDragBehavior.IsDraggingNode || WorkflowSlotConnectionBehavior.IsDraggingConnection)
                {
                    state.LastPanTotalX = e.TotalX;
                    state.LastPanTotalY = e.TotalY;
                    break;
                }

                await ApplyPanAsync(host, state, e).ConfigureAwait(false);
                break;
            case GestureStatus.Canceled:
            case GestureStatus.Completed:
                state.LastPanTotalX = 0;
                state.LastPanTotalY = 0;
                break;
        }
    }

    private static async Task ApplyPanAsync(ContentView host, SurfaceState state, PanUpdatedEventArgs e)
    {
        var viewModel = ResolveTreeViewModel(host, state);
        if (viewModel is null || state.ScrollViewer is null)
        {
            return;
        }

        var deltaX = e.TotalX - state.LastPanTotalX;
        var deltaY = e.TotalY - state.LastPanTotalY;
        state.LastPanTotalX = e.TotalX;
        state.LastPanTotalY = e.TotalY;

        var newOffsetX = state.ScrollViewer.ScrollX - deltaX;
        var newOffsetY = state.ScrollViewer.ScrollY - deltaY;
        var maxH = GetHorizontalScrollMaximum(state);
        var maxV = GetVerticalScrollMaximum(state);
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
            // Recompute max from model — canvas size was just updated via ApplyLayout
            // but MAUI layout is async so ScrollViewer.ContentSize is stale.
            maxH = GetHorizontalScrollMaximum(state);
            maxV = GetVerticalScrollMaximum(state);
        }

        var appliedOffsetX = Math.Max(0, Math.Min(newOffsetX, maxH));
        var appliedOffsetY = Math.Max(0, Math.Min(newOffsetY, maxV));
        await state.ScrollViewer.ScrollToAsync(appliedOffsetX, appliedOffsetY, false).ConfigureAwait(false);
        UpdateVisibleRegion(host, state);
    }

    private static double GetHorizontalScrollMaximum(SurfaceState state)
    {
        // MAUI layout is async — ContentSize may be stale after ApplyLayout.
        // Compute max scroll from the layout model directly instead.
        var viewModel = ResolveTreeViewModel(state.Host!, state);
        if (viewModel is null || state.ScrollViewer is null) return 0;
        return Math.Max(0, viewModel.Layout.ActualSize.Width - state.ScrollViewer.Width);
    }

    private static double GetVerticalScrollMaximum(SurfaceState state)
    {
        var viewModel = ResolveTreeViewModel(state.Host!, state);
        if (viewModel is null || state.ScrollViewer is null) return 0;
        return Math.Max(0, viewModel.Layout.ActualSize.Height - state.ScrollViewer.Height);
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

        UpdateGridDecorator(viewModel, state);
        UpdateMinimapOverlay(viewModel, state);
        viewModel.GetHelper().Viewport = new Viewport(
            state.ScrollViewer.ScrollX - viewModel.Layout.ActualOffset.Horizontal,
            state.ScrollViewer.ScrollY - viewModel.Layout.ActualOffset.Vertical,
            state.ScrollViewer.Width,
            state.ScrollViewer.Height);
    }

    private static void UpdateGridDecorator(IWorkflowTreeViewModel viewModel, SurfaceState state)
    {
        if (state.GridDecorator is not IWorkflowGridDecorator decorator || state.ScrollViewer is null)
        {
            return;
        }

        decorator.ScrollOffsetX = state.ScrollViewer.ScrollX;
        decorator.ScrollOffsetY = state.ScrollViewer.ScrollY;
        decorator.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
        decorator.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;
    }

    private static void UpdateMinimapOverlay(IWorkflowTreeViewModel viewModel, SurfaceState state)
    {
        if (state.MinimapOverlay is not IWorkflowMinimapOverlay minimap || state.ScrollViewer is null)
        {
            return;
        }

        minimap.ScrollOffsetX = state.ScrollViewer.ScrollX;
        minimap.ScrollOffsetY = state.ScrollViewer.ScrollY;
        minimap.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
        minimap.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;
        minimap.ViewportWidth = state.ScrollViewer.Width;
        minimap.ViewportHeight = state.ScrollViewer.Height;
        minimap.WorkflowTree = viewModel;
    }

    private static void ApplyPendingScrollRestore(ContentView host, SurfaceState state)
    {
        if (!state.HasPendingScrollRestore || state.IsApplyingPendingScrollRestore || state.ScrollViewer is null)
        {
            return;
        }

        state.IsApplyingPendingScrollRestore = true;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Task.Yield();

                if (state.ScrollViewer is null)
                {
                    return;
                }

                var viewModel = ResolveTreeViewModel(host, state);
                if (viewModel is null)
                {
                    return;
                }

                var targetX = state.PendingViewportX + viewModel.Layout.ActualOffset.Horizontal;
                var targetY = state.PendingViewportY + viewModel.Layout.ActualOffset.Vertical;
                targetX = Math.Max(0, Math.Min(targetX, GetHorizontalScrollMaximum(state)));
                targetY = Math.Max(0, Math.Min(targetY, GetVerticalScrollMaximum(state)));
                if (Math.Abs(state.ScrollViewer.ScrollX - targetX) > 0.5 || Math.Abs(state.ScrollViewer.ScrollY - targetY) > 0.5)
                {
                    await state.ScrollViewer.ScrollToAsync(targetX, targetY, false);
                }

                UpdateVisibleRegion(host, state);
            }
            finally
            {
                state.HasPendingScrollRestore = false;
                state.IsApplyingPendingScrollRestore = false;
            }
        });
    }
}
