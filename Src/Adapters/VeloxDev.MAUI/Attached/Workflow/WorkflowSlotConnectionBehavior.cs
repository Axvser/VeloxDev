using VeloxDev.WorkflowSystem;
using PlatformInput = Microsoft.Maui.Controls;
using Wf = VeloxDev.WorkflowSystem;

#if WINDOWS
using Microsoft.UI.Xaml;
#endif

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

public sealed class WorkflowSlotConnectionBehavior
{
    private sealed class ConnectionState
    {
        public PointerGestureRecognizer? PointerGesture { get; set; }
        public PanGestureRecognizer? PanGesture { get; set; }
        public bool IsPointerActive { get; set; }
    }

    private sealed class ActiveConnection
    {
        public View SourceView { get; init; } = null!;
        public IWorkflowSlotViewModel SourceSlot { get; init; } = null!;
        public IWorkflowTreeViewModel Tree { get; init; } = null!;
        public VisualElement? CoordinateHost { get; init; }
        public ContentView? Surface { get; init; }
        public Anchor Pointer { get; set; } = new();
        public double LastPanX { get; set; }
        public double LastPanY { get; set; }
    }

    private static ActiveConnection? _activeConnection;

    public static bool IsDraggingConnection { get; private set; }

    public static readonly BindableProperty IsEnabledProperty = BindableProperty.CreateAttached(
        "IsEnabled",
        typeof(bool),
        typeof(WorkflowSlotConnectionBehavior),
        false,
        propertyChanged: OnIsEnabledChanged);

    private static readonly BindableProperty StateProperty = BindableProperty.CreateAttached(
        "State",
        typeof(ConnectionState),
        typeof(WorkflowSlotConnectionBehavior),
        null);

    public static void SetIsDraggingConnection(bool isDraggingConnection) => IsDraggingConnection = isDraggingConnection;

    public static bool GetIsEnabled(BindableObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(BindableObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not View view)
        {
            return;
        }

        Detach(view);
        if (newValue is true)
        {
            Attach(view);
        }
    }

    private static void Attach(View view)
    {
        var pointer = new PointerGestureRecognizer
        {
            Buttons = ButtonsMask.Primary,
        };
        pointer.PointerPressed += OnPointerPressed;
        pointer.PointerMoved += OnPointerMoved;
        pointer.PointerReleased += OnPointerReleased;

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnPanUpdated;

        view.GestureRecognizers.Add(pointer);
        view.GestureRecognizers.Add(pan);
        view.SetValue(StateProperty, new ConnectionState
        {
            PointerGesture = pointer,
            PanGesture = pan,
        });
    }

    private static void Detach(View view)
    {
        if (view.GetValue(StateProperty) is not ConnectionState state)
        {
            return;
        }

        if (state.PointerGesture is not null)
        {
            state.PointerGesture.PointerPressed -= OnPointerPressed;
            state.PointerGesture.PointerMoved -= OnPointerMoved;
            state.PointerGesture.PointerReleased -= OnPointerReleased;
            view.GestureRecognizers.Remove(state.PointerGesture);
        }

        if (state.PanGesture is not null)
        {
            state.PanGesture.PanUpdated -= OnPanUpdated;
            view.GestureRecognizers.Remove(state.PanGesture);
        }

        if (ReferenceEquals(_activeConnection?.SourceView, view))
        {
            CancelActiveConnection();
        }

        view.ClearValue(StateProperty);
    }

    private static void OnPointerPressed(object? sender, PlatformInput.PointerEventArgs e)
    {
        if (sender is not View view
            || view.GetValue(StateProperty) is not ConnectionState state
            || e.Button != ButtonsMask.Primary)
        {
            return;
        }

        state.IsPointerActive = TryBeginConnection(view, e.GetPosition(FindCoordinateHost(view)));
        if (state.IsPointerActive)
        {
            TryCapturePointer(view, e);
        }
    }

    private static void OnPointerMoved(object? sender, PlatformInput.PointerEventArgs e)
    {
        if (sender is not View view
            || view.GetValue(StateProperty) is not ConnectionState { IsPointerActive: true }
            || !ReferenceEquals(_activeConnection?.SourceView, view))
        {
            return;
        }

        UpdatePointer(e.GetPosition(_activeConnection.CoordinateHost));
    }

    private static void OnPointerReleased(object? sender, PlatformInput.PointerEventArgs e)
    {
        if (sender is not View view || view.GetValue(StateProperty) is not ConnectionState state)
        {
            return;
        }

        if (state.IsPointerActive && ReferenceEquals(_activeConnection?.SourceView, view))
        {
            UpdatePointer(e.GetPosition(_activeConnection.CoordinateHost));
            CompleteActiveConnection();
            TryReleasePointer(view, e);
        }

        state.IsPointerActive = false;
    }

    private static void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (sender is not View view || view.GetValue(StateProperty) is not ConnectionState state)
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                if (ReferenceEquals(_activeConnection?.SourceView, view))
                {
                    // MAUI 库面向平台中立 TFM，原生指针捕获可能不可用；Pan 一旦开始就让它负责拖拽。
                    state.IsPointerActive = false;
                    _activeConnection.LastPanX = 0d;
                    _activeConnection.LastPanY = 0d;
                }
                else if (TryBeginConnection(view, null) && _activeConnection is not null)
                {
                    _activeConnection.LastPanX = 0d;
                    _activeConnection.LastPanY = 0d;
                }
                break;
            case GestureStatus.Running:
                if (!ReferenceEquals(_activeConnection?.SourceView, view))
                {
                    return;
                }

                var deltaX = e.TotalX - _activeConnection.LastPanX;
                var deltaY = e.TotalY - _activeConnection.LastPanY;
                _activeConnection.LastPanX = e.TotalX;
                _activeConnection.LastPanY = e.TotalY;
                UpdatePointer(new Point(
                    _activeConnection.Pointer.Horizontal + deltaX,
                    _activeConnection.Pointer.Vertical + deltaY));
                break;
            case GestureStatus.Completed:
                state.IsPointerActive = false;
                if (ReferenceEquals(_activeConnection?.SourceView, view))
                {
                    CompleteActiveConnection();
                }
                break;
            case GestureStatus.Canceled:
                state.IsPointerActive = false;
                if (ReferenceEquals(_activeConnection?.SourceView, view))
                {
                    CancelActiveConnection();
                }
                break;
        }
    }

    private static bool TryBeginConnection(View view, Point? pointer)
    {
        if (view.BindingContext is not IWorkflowSlotViewModel slot
            || slot.Parent?.Parent is not IWorkflowTreeViewModel tree)
        {
            return false;
        }

        // 这一笔按下由插槽自己转发（本家没有隧道路由相：链接层挂在交互源上的转发比这里晚，而句柄
        // 必须先有）。订阅者在插槽自己的 InputRelay 上置 PreventDefault，就是「这一笔别连」。
        // pointer 已经在画布坐标系里（见调用方的 FindCoordinateHost），量不到就用插槽自己的锚点。
        if (WorkflowSurfaceBehavior.RouteComponentPress(view, pointer)?.PreventDefault == true)
        {
            return false;
        }

        CancelActiveConnection();

        var coordinateHost = FindCoordinateHost(view);
        if (coordinateHost is not null)
        {
            SynchronizeSlotAnchor(view, coordinateHost, slot);
        }

        if (!slot.SendConnectionCommand.CanExecute(null))
        {
            return false;
        }

        slot.SendConnectionCommand.Execute(null);
        var initialPointer = pointer is null
            ? slot.Anchor
            : new Anchor(pointer.Value.X, pointer.Value.Y, slot.Anchor.Layer);

        _activeConnection = new ActiveConnection
        {
            SourceView = view,
            SourceSlot = slot,
            Tree = tree,
            CoordinateHost = coordinateHost,
            Surface = FindSurface(view),
            Pointer = initialPointer,
        };

        IsDraggingConnection = true;
        UpdatePointer(new Point(initialPointer.Horizontal, initialPointer.Vertical));
        return true;
    }

    private static void UpdatePointer(Point? pointer)
    {
        var active = _activeConnection;
        if (active is null || pointer is null)
        {
            return;
        }

        active.Pointer = new Anchor(pointer.Value.X, pointer.Value.Y, active.Pointer.Layer);
        if (active.Tree.SetPointerCommand.CanExecute(active.Pointer))
        {
            active.Tree.SetPointerCommand.Execute(active.Pointer);
        }

        if (active.Surface is not null)
        {
            WorkflowSurfaceBehavior.Refresh(active.Surface);
        }
    }

    private static void CompleteActiveConnection()
    {
        var active = _activeConnection;
        if (active is null)
        {
            return;
        }

        var viewportX = 0d;
        var viewportY = 0d;
        var preserveViewport = active.Surface is not null
            && WorkflowSurfaceBehavior.TryGetViewport(active.Surface, out viewportX, out viewportY);
        var receiver = FindReceiver(active);
        if (receiver?.ReceiveConnectionCommand.CanExecute(null) == true)
        {
            receiver.ReceiveConnectionCommand.Execute(null);
        }
        else if (active.Tree.ResetVirtualLinkCommand.CanExecute(null))
        {
            active.Tree.ResetVirtualLinkCommand.Execute(null);
        }

        var surface = active.Surface;
        ClearActiveConnection();
        if (surface is not null)
        {
            if (preserveViewport)
            {
                WorkflowSurfaceBehavior.RequestViewportRestore(surface, viewportX, viewportY);
            }
            else
            {
                WorkflowSurfaceBehavior.Refresh(surface);
            }
        }
    }

    private static IWorkflowSlotViewModel? FindReceiver(ActiveConnection active)
    {
        // 直接遍历画布子元素（都是节点 ContentView），不要对整棵可视树递归 FindDescendants；插槽视图在节点 ContentView 内，
        // 只需下钻一层就能找到带 WorkflowSlotConnectionBehavior 的视图。
        if (active.CoordinateHost is not AbsoluteLayout canvas)
        {
            return null;
        }

        const double minimumRadius = 18d;
        IWorkflowSlotViewModel? receiver = null;
        var nearestDistance = double.MaxValue;

        foreach (var child in canvas.Children)
        {
            if (child is not View nodeView)
            {
                continue;
            }

            if (GetIsEnabled(nodeView) && nodeView.BindingContext is IWorkflowSlotViewModel directSlot)
            {
                // 插槽视图直接挂在画布下（少见但支持）。
                TryMatchSlot(nodeView, directSlot, active, minimumRadius,
                    ref receiver, ref nearestDistance);
            }
            else
            {
                // 进入节点子树查找插槽视图。
                FindSlotInSubtree(nodeView, active, minimumRadius,
                    ref receiver, ref nearestDistance);
            }
        }

        return receiver;
    }

    private static void FindSlotInSubtree(
        Element root,
        ActiveConnection active,
        double minimumRadius,
        ref IWorkflowSlotViewModel? receiver,
        ref double nearestDistance)
    {
        foreach (var next in EnumerateChildren(root))
        {
            if (next is View view
                && GetIsEnabled(view)
                && view.BindingContext is IWorkflowSlotViewModel slot)
            {
                TryMatchSlot(view, slot, active, minimumRadius,
                    ref receiver, ref nearestDistance);
            }

            if (next is Element child)
            {
                FindSlotInSubtree(child, active, minimumRadius,
                    ref receiver, ref nearestDistance);
            }
        }
    }

    private static void TryMatchSlot(
        View view,
        IWorkflowSlotViewModel slot,
        ActiveConnection active,
        double minimumRadius,
        ref IWorkflowSlotViewModel? receiver,
        ref double nearestDistance)
    {
        if (!view.IsVisible
            || ReferenceEquals(slot, active.SourceSlot)
            || !ReferenceEquals(slot.Parent?.Parent, active.Tree)
            || !SynchronizeSlotAnchor(view, active.CoordinateHost, slot))
        {
            return;
        }

        var dx = slot.Anchor.Horizontal - active.Pointer.Horizontal;
        var dy = slot.Anchor.Vertical - active.Pointer.Vertical;
        var distance = (dx * dx) + (dy * dy);
        var radius = Math.Max(minimumRadius, Math.Max(view.Width, view.Height));
        if (distance <= radius * radius && distance < nearestDistance)
        {
            receiver = slot;
            nearestDistance = distance;
        }
    }

    private static void CancelActiveConnection()
    {
        var active = _activeConnection;
        if (active?.Tree.ResetVirtualLinkCommand.CanExecute(null) == true)
        {
            active.Tree.ResetVirtualLinkCommand.Execute(null);
        }

        ClearActiveConnection();
    }

    private static void ClearActiveConnection()
    {
        _activeConnection = null;
        IsDraggingConnection = false;
    }

    private static bool SynchronizeSlotAnchor(View view, VisualElement? coordinateHost, IWorkflowSlotViewModel slot)
    {
        if (coordinateHost is null || view.Width <= 0 || view.Height <= 0 || !TryGetCenterRelativeTo(view, coordinateHost, out var center))
        {
            return false;
        }

        // coordinateHost 是画布，量到的中心已是世界/画布局部坐标。
        slot.Anchor = WorkflowSurfaceMath.SlotAnchorFromCanvasLocal(center.X, center.Y, slot.Anchor.Layer);
        return true;
    }

    private static bool TryGetCenterRelativeTo(VisualElement element, VisualElement relativeTo, out Point center)
    {
        center = default;
        double x = element.Width / 2;
        double y = element.Height / 2;
        VisualElement? current = element;

        while (current is not null && !ReferenceEquals(current, relativeTo))
        {
            x += GetLeftInParent(current) + current.TranslationX;
            y += GetTopInParent(current) + current.TranslationY;
            current = current.Parent as VisualElement;
        }

        if (current is null)
        {
            return false;
        }

        center = new Point(x, y);
        return true;
    }

    private static double GetLeftInParent(VisualElement element)
    {
        if (element.Parent is AbsoluteLayout)
        {
            var bounds = AbsoluteLayout.GetLayoutBounds(element);
            if (!double.IsNaN(bounds.X))
            {
                return bounds.X;
            }
        }

        return element.X;
    }

    private static double GetTopInParent(VisualElement element)
    {
        if (element.Parent is AbsoluteLayout)
        {
            var bounds = AbsoluteLayout.GetLayoutBounds(element);
            if (!double.IsNaN(bounds.Y))
            {
                return bounds.Y;
            }
        }

        return element.Y;
    }

    private static VisualElement? FindCoordinateHost(Element element)
    {
        for (Element? current = element; current is not null; current = current.Parent)
        {
            if (current is AbsoluteLayout layout)
            {
                return layout;
            }
        }

        return null;
    }

    private static ContentView? FindSurface(Element element)
    {
        for (Element? current = element; current is not null; current = current.Parent)
        {
            if (current is ContentView contentView && WorkflowSurfaceBehavior.GetIsEnabled(contentView))
            {
                return contentView;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindDescendants<T>(Element parent) where T : Element
    {
        foreach (var child in EnumerateChildren(parent))
        {
            if (child is T result)
            {
                yield return result;
            }

            foreach (var descendant in FindDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static IEnumerable<Element> EnumerateChildren(Element parent)
    {
        switch (parent)
        {
            case Layout layout:
                foreach (var child in layout.Children.OfType<Element>())
                {
                    yield return child;
                }
                break;
            case ContentView { Content: Element content }:
                yield return content;
                break;
            case Border { Content: Element content }:
                yield return content;
                break;
            case ScrollView { Content: Element content }:
                yield return content;
                break;
        }
    }

#if WINDOWS
    private static void TryCapturePointer(View view, PlatformInput.PointerEventArgs e)
    {
        if (view.Handler?.PlatformView is UIElement element
            && e.PlatformArgs?.PointerRoutedEventArgs is { Pointer: { } pointer })
        {
            element.CapturePointer(pointer);
        }
    }

    private static void TryReleasePointer(View view, PlatformInput.PointerEventArgs e)
    {
        if (view.Handler?.PlatformView is UIElement element
            && e.PlatformArgs?.PointerRoutedEventArgs is { Pointer: { } pointer })
        {
            element.ReleasePointerCapture(pointer);
        }
    }
#else
    private static void TryCapturePointer(View view, PlatformInput.PointerEventArgs e)
    {
    }

    private static void TryReleasePointer(View view, PlatformInput.PointerEventArgs e)
    {
    }
#endif
}
