using Microsoft.Maui.Controls.Shapes;
using VeloxDev.WorkflowSystem;

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
        if (BindingContext is not IWorkflowSlotViewModel slot || FindHost() is not IWorkflowSurfaceHost host)
        {
            return;
        }

        if (!TryGetPointerAnchor(e, slot, out var anchor))
        {
            return;
        }

        TryCapturePointer(e);
        WorkflowSlotConnectionBehavior.SetIsDraggingConnection(true);
        host.BeginConnection(slot);
        host.UpdateConnectionPointer(anchor);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!WorkflowSlotConnectionBehavior.IsDraggingConnection
            || BindingContext is not IWorkflowSlotViewModel slot
            || FindHost() is not IWorkflowSurfaceHost host)
        {
            return;
        }

        if (!TryGetPointerAnchor(e, slot, out var anchor))
        {
            return;
        }

        host.UpdateConnectionPointer(anchor);
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        if (BindingContext is not IWorkflowSlotViewModel slot || FindHost() is not IWorkflowSurfaceHost host)
        {
            return;
        }

        var anchor = TryGetPointerAnchor(e, slot, out var pointerAnchor) ? pointerAnchor : slot.Anchor;
        host.CompleteConnection(anchor, slot);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            TryReleasePointer(e);
            WorkflowSlotConnectionBehavior.SetIsDraggingConnection(false);
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

    private IWorkflowSurfaceHost? FindHost()
    {
        Element? current = this;
        while (current is not null)
        {
            if (current is IWorkflowSurfaceHost host)
            {
                return host;
            }

            current = current.Parent;
        }

        return null;
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
