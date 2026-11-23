using System.Diagnostics;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VisualCanvas = VeloxDev.WinForms.Xaml.Visuals.Controls.Canvas;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// WPF-shaped attached behavior that turns a press on the marked element into a node drag.
/// </summary>
/// <remarks>
/// Attach it to the card's title row. The node is found through the visual ancestors' data context; the drag
/// delta is measured in the coordinate host's local space (<see cref="CoordinateHostNameProperty"/>, or the
/// nearest visual canvas), which is the world space the canvas camera maps to.
/// </remarks>
public sealed class WorkflowNodeDragBehavior
{
    /// <summary>Whether the element (and its descendants) may start a node drag.</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WorkflowNodeDragBehavior),
        new PropertyMetadata(false));

    /// <summary>Name of the element that supplies the coordinate space for the drag delta.</summary>
    public static readonly DependencyProperty CoordinateHostNameProperty = DependencyProperty.RegisterAttached(
        "CoordinateHostName", typeof(string), typeof(WorkflowNodeDragBehavior),
        new PropertyMetadata(null));

    private static readonly ConditionalWeakTable<VisualElement, DragState> States = new();

    private WorkflowNodeDragBehavior()
    {
    }

    /// <summary>Reads the drag switch.</summary>
    public static bool GetIsEnabled(VisualElement element) => (bool)element.GetValue(IsEnabledProperty)!;

    /// <summary>Writes the drag switch.</summary>
    public static void SetIsEnabled(VisualElement element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>Reads the coordinate-host name.</summary>
    public static string? GetCoordinateHostName(VisualElement element)
        => (string?)element.GetValue(CoordinateHostNameProperty);

    /// <summary>Writes the coordinate-host name.</summary>
    public static void SetCoordinateHostName(VisualElement element, string? value)
        => element.SetValue(CoordinateHostNameProperty, value);

    /// <summary>Attaches or detaches the behavior according to the element's current attached values.</summary>
    public static void Ensure(VisualElement element)
    {
        if (element.IsSet(IsEnabledProperty) && GetIsEnabled(element))
        {
            States.GetValue(element, static owner => new DragState(owner)).Subscribe();
        }
        else if (States.TryGetValue(element, out var state))
        {
            state.Unsubscribe();
        }
    }

    private sealed class DragState
    {
        // 指针事件可以到 100+Hz，而每次 MoveCommand 会让整棵视觉树重新布局、整面重绘。
        // 把同一帧内的位移合起来，约 60Hz 提交一次，拖动的手感不变，开销减半。
        private const double ApplyIntervalMs = 1000d / 60d;

        private readonly VisualElement owner;
        private readonly Stopwatch applyClock = new();
        private INodeComponent? node;
        private VisualElement? coordinateHost;
        private PointF last;
        private float pendingX;
        private float pendingY;
        private bool dragging;
        private bool subscribed;

        public DragState(VisualElement owner)
        {
            this.owner = owner;
        }

        public void Subscribe()
        {
            // Ensure runs on every layout pass. Re-subscribing must not cancel an in-flight drag.
            if (subscribed)
            {
                return;
            }

            subscribed = true;
            owner.PointerPressed += OnPointerPressed;
            owner.PointerMoved += OnPointerMoved;
            owner.PointerReleased += OnPointerReleased;
        }

        public void Unsubscribe()
        {
            if (!subscribed)
            {
                return;
            }

            subscribed = false;
            owner.PointerPressed -= OnPointerPressed;
            owner.PointerMoved -= OnPointerMoved;
            owner.PointerReleased -= OnPointerReleased;
            dragging = false;
            node = null;
            coordinateHost = null;
        }

        private void OnPointerPressed(object? sender, VisualPointerEventArgs args)
        {
            dragging = false;
            node = ResolveNode(owner);
            coordinateHost = ResolveCoordinateHost(owner, GetCoordinateHostName(owner));
            if (node is null || coordinateHost is null || args.Button != MouseButtons.Left)
            {
                return;
            }

            if (WorkflowSurfaceBehavior.GetPressHandle(owner)?.PreventDefault == true)
            {
                return;
            }

            last = args.GetPosition(coordinateHost);
            pendingX = 0f;
            pendingY = 0f;
            applyClock.Restart();
            dragging = true;
            args.Handled = true;
        }

        private void OnPointerMoved(object? sender, VisualPointerEventArgs args)
        {
            if (!dragging || node is null || coordinateHost is null)
            {
                return;
            }

            if ((args.Buttons & MouseButtons.Left) == 0)
            {
                Cancel();
                return;
            }

            var current = args.GetPosition(coordinateHost);
            pendingX += current.X - last.X;
            pendingY += current.Y - last.Y;
            last = current;

            if (applyClock.Elapsed.TotalMilliseconds >= ApplyIntervalMs)
            {
                ApplyPending();
            }

            args.Handled = true;
        }

        private void OnPointerReleased(object? sender, VisualPointerEventArgs args)
        {
            if (!dragging)
            {
                return;
            }

            ApplyPending();
            Cancel();
            args.Handled = true;
        }

        private void ApplyPending()
        {
            if (node is null || (Math.Abs(pendingX) <= 0.001f && Math.Abs(pendingY) <= 0.001f))
            {
                return;
            }

            var dx = pendingX;
            var dy = pendingY;
            pendingX = 0f;
            pendingY = 0f;
            applyClock.Restart();

            if (node.MoveCommand.CanExecute(new Offset(dx, dy)))
            {
                node.MoveCommand.Execute(new Offset(dx, dy));
            }
        }

        private void Cancel()
        {
            dragging = false;
            node = null;
            coordinateHost = null;
            pendingX = 0f;
            pendingY = 0f;
        }
    }

    private static INodeComponent? ResolveNode(VisualElement element)
    {
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (current.DataContext is INodeComponent node)
            {
                return node;
            }
        }

        return null;
    }

    private static VisualElement? ResolveCoordinateHost(VisualElement element, string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            for (var current = element; current is not null; current = current.ParentElement)
            {
                if (string.Equals(current.Name, name, StringComparison.Ordinal))
                {
                    return current;
                }
            }
        }

        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (current is VisualCanvas)
            {
                return current;
            }
        }

        return element.Surface?.Root as VisualElement;
    }
}
