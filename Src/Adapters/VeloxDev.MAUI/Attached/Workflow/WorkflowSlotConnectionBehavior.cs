using VeloxDev.WorkflowSystem;

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

    private static void OnPointerPressed(object? sender, PointerEventArgs e)
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

    private static void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not View view
            || view.GetValue(StateProperty) is not ConnectionState { IsPointerActive: true }
            || !ReferenceEquals(_activeConnection?.SourceView, view))
        {
            return;
        }

        UpdatePointer(e.GetPosition(_activeConnection.CoordinateHost));
    }

    private static void OnPointerReleased(object? sender, PointerEventArgs e)
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
                    // MAUI libraries target a platform-neutral TFM, so native pointer
                    // capture may be unavailable. Once Pan starts, let it own the drag.
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
        // Iterate canvas children directly (they are node ContentViews) rather than
        // using recursive FindDescendants over the entire visual tree. Slot views
        // live inside node ContentViews, so we only need to descend one level into
        // each node's content to find Views with WorkflowSlotConnectionBehavior.
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
                // Slot view is a direct child of the canvas (unusual but supported).
                TryMatchSlot(nodeView, directSlot, active, minimumRadius,
                    ref receiver, ref nearestDistance);
            }
            else
            {
                // Walk into the node's subtree to find slot views.
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

    private static bool SynchronizeSlotAnchor(View view, VisualElement coordinateHost, IWorkflowSlotViewModel slot)
    {
        if (view.Width <= 0 || view.Height <= 0 || !TryGetCenterRelativeTo(view, coordinateHost, out var center))
        {
            return false;
        }

        slot.Anchor = new Anchor(center.X, center.Y, slot.Anchor.Layer);
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
    private static void TryCapturePointer(View view, PointerEventArgs e)
    {
        if (view.Handler?.PlatformView is UIElement element
            && e.PlatformArgs?.PointerRoutedEventArgs is { Pointer: { } pointer })
        {
            element.CapturePointer(pointer);
        }
    }

    private static void TryReleasePointer(View view, PointerEventArgs e)
    {
        if (view.Handler?.PlatformView is UIElement element
            && e.PlatformArgs?.PointerRoutedEventArgs is { Pointer: { } pointer })
        {
            element.ReleasePointerCapture(pointer);
        }
    }
#else
    private static void TryCapturePointer(View view, PointerEventArgs e)
    {
    }

    private static void TryReleasePointer(View view, PointerEventArgs e)
    {
    }
#endif
}
