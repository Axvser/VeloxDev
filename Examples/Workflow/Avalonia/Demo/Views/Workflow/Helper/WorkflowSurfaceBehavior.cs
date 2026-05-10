using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Demo.ViewModels;
using System;
using System.Linq;
using VeloxDev.WorkflowSystem;

namespace Demo;

public sealed class WorkflowSurfaceBehavior : AvaloniaObject
{
    private sealed class SurfaceState
    {
        public bool IsPanning { get; set; }
        public Point PanStart { get; set; }
        public Vector PanStartOffset { get; set; }
        public ScrollViewer? ScrollViewer { get; set; }
        public Canvas? Canvas { get; set; }
        public WorkflowGridDecorator? GridDecorator { get; set; }
        public Control? PointerPressSource { get; set; }
    }

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, bool>("IsEnabled");

    public static readonly AttachedProperty<string?> ScrollViewerNameProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, string?>("ScrollViewerName");

    public static readonly AttachedProperty<string?> CanvasNameProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, string?>("CanvasName");

    public static readonly AttachedProperty<string?> GridDecoratorNameProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, string?>("GridDecoratorName");

    public static readonly AttachedProperty<string?> PointerPressSourceNameProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, string?>("PointerPressSourceName");

    private static readonly AttachedProperty<SurfaceState?> StateProperty =
        AvaloniaProperty.RegisterAttached<WorkflowSurfaceBehavior, UserControl, SurfaceState?>("State");

    static WorkflowSurfaceBehavior()
    {
        IsEnabledProperty.Changed.AddClassHandler<UserControl>(OnIsEnabledChanged);
    }

    public static bool GetIsEnabled(AvaloniaObject element) => element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(AvaloniaObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static string? GetScrollViewerName(AvaloniaObject element) => element.GetValue(ScrollViewerNameProperty);

    public static void SetScrollViewerName(AvaloniaObject element, string? value) => element.SetValue(ScrollViewerNameProperty, value);

    public static string? GetCanvasName(AvaloniaObject element) => element.GetValue(CanvasNameProperty);

    public static void SetCanvasName(AvaloniaObject element, string? value) => element.SetValue(CanvasNameProperty, value);

    public static string? GetGridDecoratorName(AvaloniaObject element) => element.GetValue(GridDecoratorNameProperty);

    public static void SetGridDecoratorName(AvaloniaObject element, string? value) => element.SetValue(GridDecoratorNameProperty, value);

    public static string? GetPointerPressSourceName(AvaloniaObject element) => element.GetValue(PointerPressSourceNameProperty);

    public static void SetPointerPressSourceName(AvaloniaObject element, string? value) => element.SetValue(PointerPressSourceNameProperty, value);

    public static void Refresh(UserControl host)
    {
        if (!GetIsEnabled(host))
            return;

        var state = host.GetValue(StateProperty) ?? new SurfaceState();
        host.SetValue(StateProperty, state);
        ResolveNamedControls(host, state);
        ApplyLayout(host, state);
        UpdateVisibleRegion(host, state);
    }

    private static void OnIsEnabledChanged(UserControl control, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
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
        control.AttachedToVisualTree += OnAttachedToVisualTree;
        control.DetachedFromVisualTree += OnDetachedFromVisualTree;
        control.DataContextChanged += OnDataContextChanged;
        control.PointerMoved += OnPointerMoved;
        control.PointerReleased += OnPointerReleased;
        ResolveNamedControls(control, state);
        Refresh(control);
    }

    private static void Detach(UserControl control)
    {
        control.AttachedToVisualTree -= OnAttachedToVisualTree;
        control.DetachedFromVisualTree -= OnDetachedFromVisualTree;
        control.DataContextChanged -= OnDataContextChanged;
        control.PointerMoved -= OnPointerMoved;
        control.PointerReleased -= OnPointerReleased;

        if (control.GetValue(StateProperty) is SurfaceState state)
            UnsubscribeResolvedControls(state);

        control.ClearValue(StateProperty);
    }

    private static void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is UserControl control)
            Refresh(control);
    }

    private static void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is UserControl control && control.GetValue(StateProperty) is SurfaceState state)
            state.IsPanning = false;
    }

    private static void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (sender is UserControl control)
            Refresh(control);
    }

    private static void ResolveNamedControls(UserControl control, SurfaceState state)
    {
        UnsubscribeResolvedControls(state);

        var scrollViewerName = GetScrollViewerName(control);
        if (!string.IsNullOrWhiteSpace(scrollViewerName))
            state.ScrollViewer = control.FindControl<ScrollViewer>(scrollViewerName);

        var canvasName = GetCanvasName(control);
        if (!string.IsNullOrWhiteSpace(canvasName))
            state.Canvas = control.FindControl<Canvas>(canvasName);

        var gridDecoratorName = GetGridDecoratorName(control);
        if (!string.IsNullOrWhiteSpace(gridDecoratorName))
            state.GridDecorator = control.FindControl<WorkflowGridDecorator>(gridDecoratorName);

        var pointerPressSourceName = GetPointerPressSourceName(control);
        if (!string.IsNullOrWhiteSpace(pointerPressSourceName))
            state.PointerPressSource = control.FindControl<Control>(pointerPressSourceName);

        if (state.PointerPressSource is not null)
            state.PointerPressSource.PointerPressed += OnPointerPressed;

        if (state.ScrollViewer is not null)
            state.ScrollViewer.ScrollChanged += OnScrollChanged;
    }

    private static void UnsubscribeResolvedControls(SurfaceState state)
    {
        if (state.PointerPressSource is not null)
            state.PointerPressSource.PointerPressed -= OnPointerPressed;

        if (state.ScrollViewer is not null)
            state.ScrollViewer.ScrollChanged -= OnScrollChanged;

        state.PointerPressSource = null;
        state.ScrollViewer = null;
        state.Canvas = null;
        state.GridDecorator = null;
    }

    private static void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control source)
            return;

        var host = source.GetVisualAncestors().OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host is null || host.GetValue(StateProperty) is not SurfaceState state || state.ScrollViewer is null)
            return;

        if (!e.GetCurrentPoint(state.ScrollViewer).Properties.IsMiddleButtonPressed)
            return;

        state.IsPanning = true;
        state.PanStart = e.GetPosition(host);
        state.PanStartOffset = state.ScrollViewer.Offset;
        e.Handled = true;
    }

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
            return;

        OnCanvasPanMoved(host, state, e);
        if (state.IsPanning)
            return;

        if (host.DataContext is not TreeViewModel viewModel || state.Canvas is null)
            return;

        var point = e.GetPosition(state.Canvas);
        viewModel.SetPointerCommand.Execute(new Anchor(
            point.X - viewModel.Layout.ActualOffset.Horizontal,
            point.Y - viewModel.Layout.ActualOffset.Vertical,
            0));
    }

    private static void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not UserControl host || host.GetValue(StateProperty) is not SurfaceState state)
            return;

        if (state.IsPanning)
        {
            state.IsPanning = false;
            e.Handled = true;
            return;
        }

        if (host.DataContext is not TreeViewModel viewModel)
            return;

        viewModel.VirtualLink.Sender.State &= ~SlotState.PreviewSender;
        viewModel.ResetVirtualLinkCommand.Execute(null);
    }

    private static void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (sender is not ScrollViewer viewer)
            return;

        var host = viewer.GetVisualAncestors().OfType<UserControl>().FirstOrDefault(GetIsEnabled);
        if (host is not null)
            Refresh(host);
    }

    private static void OnCanvasPanMoved(UserControl host, SurfaceState state, PointerEventArgs e)
    {
        if (!state.IsPanning || state.ScrollViewer is null || host.DataContext is not TreeViewModel viewModel)
            return;

        var current = e.GetPosition(host);
        var newOffsetX = state.PanStartOffset.X + (state.PanStart.X - current.X);
        var newOffsetY = state.PanStartOffset.Y + (state.PanStart.Y - current.Y);
        var maxH = GetHorizontalScrollMaximum(state.ScrollViewer);
        var maxV = GetVerticalScrollMaximum(state.ScrollViewer);
        bool layoutChanged = false;

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
            maxH = GetHorizontalScrollMaximum(state.ScrollViewer);
            maxV = GetVerticalScrollMaximum(state.ScrollViewer);
            newOffsetX = Math.Min(newOffsetX, maxH);
            newOffsetY = Math.Min(newOffsetY, maxV);
            state.PanStart = current;
            state.PanStartOffset = new Vector(
                Math.Max(0, Math.Min(newOffsetX, maxH)),
                Math.Max(0, Math.Min(newOffsetY, maxV)));
        }

        state.ScrollViewer.Offset = new Vector(
            Math.Max(0, Math.Min(newOffsetX, maxH)),
            Math.Max(0, Math.Min(newOffsetY, maxV)));

        UpdateVisibleRegion(host, state);
        e.Handled = true;
    }

    private static void ApplyLayout(UserControl host, SurfaceState state)
    {
        if (host.DataContext is not TreeViewModel viewModel || state.Canvas is null)
            return;

        state.Canvas.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        var transform = new TransformGroup
        {
            Children = [
                new TranslateTransform(
                    viewModel.Layout.ActualOffset.Horizontal,
                    viewModel.Layout.ActualOffset.Vertical)
            ]
        };

        if (host is WorkflowView workflowView)
            workflowView.CanvasTransform = transform;

        UpdateGridDecorator(viewModel, state);
    }

    private static void UpdateVisibleRegion(UserControl host, SurfaceState state)
    {
        if (host.DataContext is not TreeViewModel viewModel || state.ScrollViewer is null)
            return;

        UpdateGridDecorator(viewModel, state);
        viewModel.GetHelper().Viewport = new Viewport(
            state.ScrollViewer.Offset.X - viewModel.Layout.ActualOffset.Horizontal,
            state.ScrollViewer.Offset.Y - viewModel.Layout.ActualOffset.Vertical,
            state.ScrollViewer.Viewport.Width,
            state.ScrollViewer.Viewport.Height);
    }

    private static void UpdateGridDecorator(TreeViewModel viewModel, SurfaceState state)
    {
        if (state.GridDecorator is null || state.ScrollViewer is null)
            return;

        state.GridDecorator.ScrollOffsetX = state.ScrollViewer.Offset.X;
        state.GridDecorator.ScrollOffsetY = state.ScrollViewer.Offset.Y;
        state.GridDecorator.ContentOffsetX = viewModel.Layout.ActualOffset.Horizontal;
        state.GridDecorator.ContentOffsetY = viewModel.Layout.ActualOffset.Vertical;
    }

    private static double GetHorizontalScrollMaximum(ScrollViewer scrollViewer)
        => Math.Max(0, scrollViewer.Extent.Width - scrollViewer.Viewport.Width);

    private static double GetVerticalScrollMaximum(ScrollViewer scrollViewer)
        => Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
}
