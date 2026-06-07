using Microsoft.Maui.Controls.Shapes;
using System.Reflection;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

#if WINDOWS
using Microsoft.UI.Xaml;
#endif

namespace Demo.Controls;

public partial class SlotView : ContentView
{
    private PointerGestureRecognizer? _pointerGesture;

    public static readonly BindableProperty SlotStateProperty = BindableProperty.Create(
        nameof(SlotState),
        typeof(SlotState),
        typeof(SlotView),
        SlotState.StandBy,
        propertyChanged: OnSlotStateChanged);

    public SlotView()
    {
        InitializeComponent();
        IconView.Drawable = new SlotDrawable(this);
    }

    public SlotState SlotState
    {
        get => (SlotState)GetValue(SlotStateProperty);
        set => SetValue(SlotStateProperty, value);
    }

    protected override void OnParentSet()
    {
        base.OnParentSet();

        if (_pointerGesture is not null)
        {
            _pointerGesture.PointerPressed -= OnPointerPressed;
            _pointerGesture.PointerMoved -= OnPointerMoved;
            _pointerGesture.PointerReleased -= OnPointerReleased;
            GestureRecognizers.Remove(_pointerGesture);
        }

        _pointerGesture = new PointerGestureRecognizer();
        _pointerGesture.PointerPressed += OnPointerPressed;
        _pointerGesture.PointerMoved += OnPointerMoved;
        _pointerGesture.PointerReleased += OnPointerReleased;
        GestureRecognizers.Add(_pointerGesture);
    }

    private static void OnSlotStateChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is SlotView slotView)
        {
            slotView.IconView.Invalidate();
        }
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        var slot = BindingContext as IWorkflowSlotViewModel;
        var host = FindHost();
        if (slot is null || host is null)
        {
            return;
        }

        if (!TryGetPointerAnchor(e, slot, out var anchor))
        {
            return;
        }

        if (TryGetSlotCenterAnchor(e, slot, out var sourceAnchor))
        {
            slot.Anchor = sourceAnchor;
        }

        TryCapturePointer(e);
        WorkflowBehaviors.WorkflowSlotConnectionBehavior.SetIsDraggingConnection(true);
        if (slot.SendConnectionCommand.CanExecute(null))
        {
            slot.SendConnectionCommand.Execute(null);
        }

        InvokeHostMethod(host, "UpdateConnectionPointer", anchor);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var slot = BindingContext as IWorkflowSlotViewModel;
        var host = FindHost();
        if (!WorkflowBehaviors.WorkflowSlotConnectionBehavior.IsDraggingConnection
            || slot is null
            || host is null)
        {
            return;
        }

        if (!TryGetPointerAnchor(e, slot, out var anchor))
        {
            return;
        }

        InvokeHostMethod(host, "UpdateConnectionPointer", anchor);
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        var slot = BindingContext as IWorkflowSlotViewModel;
        var host = FindHost();
        if (slot is null || host is null)
        {
            return;
        }

        var anchor = TryGetPointerAnchor(e, slot, out var pointerAnchor) ? pointerAnchor : slot.Anchor;
        InvokeHostMethod(host, "CompleteConnection", anchor, slot);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            TryReleasePointer(e);
            WorkflowBehaviors.WorkflowSlotConnectionBehavior.SetIsDraggingConnection(false);
        });
    }

    private bool TryGetPointerAnchor(PointerEventArgs e, IWorkflowSlotViewModel slot, out Anchor anchor)
    {
        anchor = slot.Anchor;
        var coordinateHost = FindCoordinateHost();
        var position = coordinateHost is null ? e.GetPosition(this) : e.GetPosition(coordinateHost);
        if (position is null)
        {
            return false;
        }

        anchor = coordinateHost is null
            ? new Anchor(slot.Anchor.Horizontal + position.Value.X, slot.Anchor.Vertical + position.Value.Y, slot.Anchor.Layer)
            : new Anchor(position.Value.X, position.Value.Y, slot.Anchor.Layer);
        return true;
    }

    private bool TryGetSlotCenterAnchor(PointerEventArgs e, IWorkflowSlotViewModel slot, out Anchor anchor)
    {
        anchor = slot.Anchor;
        var coordinateHost = FindCoordinateHost();
        if (coordinateHost is null || Width <= 0 || Height <= 0)
        {
            return false;
        }

        var pointerOnHost = e.GetPosition(coordinateHost);
        var pointerOnSlot = e.GetPosition(this);
        if (pointerOnHost is null || pointerOnSlot is null)
        {
            return false;
        }

        anchor = new Anchor(
            pointerOnHost.Value.X - pointerOnSlot.Value.X + (Width / 2),
            pointerOnHost.Value.Y - pointerOnSlot.Value.Y + (Height / 2),
            slot.Anchor.Layer);
        return true;
    }

    public bool SynchronizeAnchor()
    {
        if (BindingContext is not IWorkflowSlotViewModel slot
            || FindCoordinateHost() is not VisualElement coordinateHost
            || Width <= 0
            || Height <= 0
            || !TryGetCenterRelativeTo(coordinateHost, out var center))
        {
            return false;
        }

        slot.Anchor = new Anchor(center.X, center.Y, slot.Anchor.Layer);
        return true;
    }

    private bool TryGetCenterRelativeTo(VisualElement relativeTo, out Point center)
    {
        center = default;
        double x = Width / 2;
        double y = Height / 2;
        VisualElement? current = this;

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

    private ContentView? FindHost()
    {
        Element? current = this;
        while (current is not null)
        {
            if (current is ContentView host && HasHostMethod(host, "UpdateConnectionPointer", typeof(Anchor)) && HasHostMethod(host, "CompleteConnection", typeof(Anchor), typeof(IWorkflowSlotViewModel)))
            {
                return host;
            }

            current = current.Parent;
        }

        return null;
    }

    private static bool HasHostMethod(object host, string methodName, params Type[] parameterTypes)
        => host.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameterTypes, null) is not null;

    private static void InvokeHostMethod(object host, string methodName, params object[] arguments)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(arguments);

        var parameterTypes = arguments.Select(static argument => argument.GetType()).ToArray();
        var method = host.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameterTypes, null);
        if (method is null)
        {
            throw new InvalidOperationException($"Host method '{methodName}' was not found on type '{host.GetType().FullName}'.");
        }

        method.Invoke(host, arguments);
    }

    private VisualElement? FindCoordinateHost()
    {
        Element? current = this;
        while (current is not null)
        {
            if (current is AbsoluteLayout layout)
            {
                return layout;
            }

            current = current.Parent;
        }

        return null;
    }

#if WINDOWS
    private void TryCapturePointer(PointerEventArgs e)
    {
        if (Handler?.PlatformView is UIElement element && e.PlatformArgs?.PointerRoutedEventArgs is { Pointer: { } pointer })
        {
            element.CapturePointer(pointer);
        }
    }

    private void TryReleasePointer(PointerEventArgs e)
    {
        if (Handler?.PlatformView is UIElement element && e.PlatformArgs?.PointerRoutedEventArgs is { Pointer: { } pointer })
        {
            element.ReleasePointerCapture(pointer);
        }
    }
#else
    private void TryCapturePointer(PointerEventArgs e)
    {
    }

    private void TryReleasePointer(PointerEventArgs e)
    {
    }
#endif

    private sealed class SlotDrawable(SlotView owner) : IDrawable
    {
        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var color = ResolveSlotColor(owner.SlotState);
            var centerX = dirtyRect.Center.X;
            var centerY = dirtyRect.Center.Y;
            var radius = Math.Min(dirtyRect.Width, dirtyRect.Height) / 2f - 1f;

            canvas.FillColor = color;
            canvas.FillCircle(centerX, centerY, radius);
            canvas.StrokeColor = Colors.White;
            canvas.StrokeSize = 1.5f;
            canvas.DrawCircle(centerX, centerY, radius);
        }

        private static Color ResolveSlotColor(SlotState state)
            => state switch
            {
                var value when value.HasFlag(SlotState.Sender) && value.HasFlag(SlotState.Receiver) => Colors.Violet,
                var value when value.HasFlag(SlotState.Sender) => Colors.Tomato,
                var value when value.HasFlag(SlotState.Receiver) => Colors.Lime,
                _ => Colors.White,
            };
    }
}
