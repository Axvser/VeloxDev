using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using DrawingPointF = System.Drawing.PointF;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>What the pointer resolved to on a <see cref="VisualWorkflowSurface"/>, before it is routed to Core.</summary>
public readonly struct VisualWorkflowHit
{
    internal VisualWorkflowHit(
        Wf.Anchor model,
        bool inCanvas,
        IWorkflowComponent? target,
        INodeComponent? node,
        ISlotComponent? slot,
        ILinkComponent? link)
    {
        Model = model;
        InCanvas = inCanvas;
        Target = target;
        Node = node;
        Slot = slot;
        Link = link;
    }

    /// <summary>The pointer position in canvas-local (collapsed model) space.</summary>
    public Wf.Anchor Model { get; }

    /// <summary>Whether the hit landed inside the workflow canvas rather than on an overlay.</summary>
    public bool InCanvas { get; }

    /// <summary>The component the pointer is on, or <see langword="null"/> for empty canvas.</summary>
    public IWorkflowComponent? Target { get; }

    /// <summary>The node under the pointer, or <see langword="null"/>.</summary>
    public INodeComponent? Node { get; }

    /// <summary>The slot under the pointer, or <see langword="null"/>.</summary>
    public ISlotComponent? Slot { get; }

    /// <summary>The link under the pointer, or <see langword="null"/>.</summary>
    public ILinkComponent? Link { get; }
}

/// <summary>The outcomes of routing one input event: the resolved hit and the handle the route filled in.</summary>
public readonly struct VisualWorkflowRouteResult
{
    internal VisualWorkflowRouteResult(VisualWorkflowHit hit, WorkflowEventHandle handle)
    {
        Hit = hit;
        Handle = handle;
    }

    /// <summary>What the pointer resolved to.</summary>
    public VisualWorkflowHit Hit { get; }

    /// <summary>The handle shared with every subscriber of this one action.</summary>
    public WorkflowEventHandle Handle { get; }
}

/// <summary>
/// Translates the visual backend's pointer and keyboard arguments into the platform-neutral workflow input route.
/// </summary>
/// <remarks>
/// The router resolves a target by walking the visual hit's ancestor chain to a slot or node view and then, when
/// neither was hit, by asking Core's shared link hit-test against the curves the link layer published. Enter/leave
/// ordering is not re-implemented here: every event is routed as it happens, and
/// <see cref="WorkflowInput"/> emits the exited/entered pair for the target change.
/// </remarks>
public sealed class VisualWorkflowInputRouter
{
    private readonly VisualWorkflowSurface _surface;

    /// <summary>Creates a router for one surface.</summary>
    /// <param name="surface">The surface whose canvas, link layer and tree are used.</param>
    public VisualWorkflowInputRouter(VisualWorkflowSurface surface)
        => _surface = surface ?? throw new ArgumentNullException(nameof(surface));

    /// <summary>How far from a published curve a point still counts as on the link, in canvas units.</summary>
    public double HitRadius { get; set; } = LinkHitTestEx.DefaultHitRadius;

    /// <summary>Resolves a surface point to the component under it and the matching model position.</summary>
    /// <param name="surfacePoint">The point in surface client coordinates.</param>
    public VisualWorkflowHit Resolve(DrawingPointF surfacePoint)
    {
        var canvas = _surface.Canvas;
        var element = _surface.FindElementAt(surfacePoint);
        var model = canvas.SurfaceToModel(surfacePoint);

        if (!IsWithin(canvas, element))
        {
            return new VisualWorkflowHit(model, inCanvas: false, null, null, null, null);
        }

        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (VisualWorkflowSlotBehavior.GetSlot(current) is { } slot)
            {
                return new VisualWorkflowHit(model, true, slot, slot.Parent, slot, null);
            }

            var node = VisualWorkflowNodeBehavior.GetNode(current);
            if (node is not null)
            {
                return new VisualWorkflowHit(model, true, node, node, null, null);
            }
        }

        var link = _surface.Tree?.HitTestVisibleLinks(model.Horizontal, model.Vertical, HitRadius);
        return new VisualWorkflowHit(model, true, link, null, null, link);
    }

    /// <summary>Routes a pointer press.</summary>
    /// <param name="args">The visual backend's pointer data.</param>
    public VisualWorkflowRouteResult RoutePressed(VisualPointerEventArgs args)
    {
        var hit = Resolve(args.Position);
        var handle = new WorkflowEventHandle();
        _surface.Input?.Route(new PointerPressedEventArgs(
            hit.Model,
            ToModifiers(args.Modifiers),
            _surface,
            hit.Target,
            ToButton(args.Button),
            args.ClickCount,
            handle));
        return new VisualWorkflowRouteResult(hit, handle);
    }

    /// <summary>Routes a pointer move.</summary>
    /// <param name="args">The visual backend's pointer data.</param>
    public VisualWorkflowRouteResult RouteMoved(VisualPointerEventArgs args)
    {
        var hit = Resolve(args.Position);
        var handle = new WorkflowEventHandle();
        _surface.Input?.Route(new PointerMovedEventArgs(
            hit.Model,
            ToModifiers(args.Modifiers),
            _surface,
            hit.Target,
            handle));
        return new VisualWorkflowRouteResult(hit, handle);
    }

    /// <summary>Routes a pointer release.</summary>
    /// <param name="args">The visual backend's pointer data.</param>
    public VisualWorkflowRouteResult RouteReleased(VisualPointerEventArgs args)
    {
        var hit = Resolve(args.Position);
        var handle = new WorkflowEventHandle();
        _surface.Input?.Route(new PointerReleasedEventArgs(
            hit.Model,
            ToModifiers(args.Modifiers),
            _surface,
            hit.Target,
            ToButton(args.Button),
            1,
            handle));
        return new VisualWorkflowRouteResult(hit, handle);
    }

    /// <summary>Routes a wheel turn.</summary>
    /// <param name="args">The visual backend's pointer data; <see cref="VisualPointerEventArgs.Delta"/> carries the wheel delta.</param>
    public VisualWorkflowRouteResult RouteWheel(VisualPointerEventArgs args)
    {
        var hit = Resolve(args.Position);
        var handle = new WorkflowEventHandle();
        _surface.Input?.Route(new PointerWheelEventArgs(
            hit.Model,
            ToModifiers(args.Modifiers),
            _surface,
            hit.Target,
            0d,
            args.Delta / 120d,
            handle));
        return new VisualWorkflowRouteResult(hit, handle);
    }

    /// <summary>Routes a key down to the component the pointer is currently on.</summary>
    /// <param name="args">The visual backend's key data.</param>
    public VisualWorkflowRouteResult RouteKeyDown(VisualKeyEventArgs args)
    {
        var handle = new WorkflowEventHandle();
        if (_surface.Input is not { } input)
        {
            return new VisualWorkflowRouteResult(default, handle);
        }

        input.Route(new KeyDownEventArgs(
            ToInputKey(args.KeyCode),
            (int)args.KeyCode,
            ToModifiers(args.Modifiers),
            args.IsRepeat,
            _surface,
            input.PointerTarget,
            handle));
        return new VisualWorkflowRouteResult(default, handle);
    }

    /// <summary>Routes a key up to the component the pointer is currently on.</summary>
    /// <param name="args">The visual backend's key data.</param>
    public VisualWorkflowRouteResult RouteKeyUp(VisualKeyEventArgs args)
    {
        var handle = new WorkflowEventHandle();
        if (_surface.Input is not { } input)
        {
            return new VisualWorkflowRouteResult(default, handle);
        }

        input.Route(new KeyUpEventArgs(
            ToInputKey(args.KeyCode),
            (int)args.KeyCode,
            ToModifiers(args.Modifiers),
            args.IsRepeat,
            _surface,
            input.PointerTarget,
            handle));
        return new VisualWorkflowRouteResult(default, handle);
    }

    /// <summary>Reports that the pointer left the whole surface, so the route drops its target.</summary>
    public void RouteExit()
    {
        _surface.Input?.Route(new PointerExitedEventArgs(
            new Wf.Anchor(), InputModifiers.None, _surface, null, new WorkflowEventHandle()));
    }

    /// <summary>Converts WinForms modifiers into the workflow input modifiers.</summary>
    /// <param name="keys">The modifier keys held.</param>
    public static InputModifiers ToModifiers(Keys keys)
    {
        var modifiers = InputModifiers.None;
        if ((keys & Keys.Control) != 0) modifiers |= InputModifiers.Control;
        if ((keys & Keys.Shift) != 0) modifiers |= InputModifiers.Shift;
        if ((keys & Keys.Alt) != 0) modifiers |= InputModifiers.Alt;
        return modifiers;
    }

    /// <summary>Converts a WinForms mouse button into the workflow input button.</summary>
    /// <param name="button">The mouse button.</param>
    public static MouseButton ToButton(MouseButtons button) => button switch
    {
        MouseButtons.Left => MouseButton.Left,
        MouseButtons.Right => MouseButton.Right,
        MouseButtons.Middle => MouseButton.Middle,
        MouseButtons.XButton1 => MouseButton.XButton1,
        MouseButtons.XButton2 => MouseButton.XButton2,
        _ => MouseButton.None,
    };

    /// <summary>Converts a WinForms key into the curated workflow input key.</summary>
    /// <param name="key">The key code.</param>
    public static InputKey ToInputKey(Keys key) => (key & Keys.KeyCode) switch
    {
        Keys.Delete => InputKey.Delete,
        Keys.Escape => InputKey.Escape,
        Keys.Tab => InputKey.Tab,
        Keys.Enter => InputKey.Enter,
        Keys.Space => InputKey.Space,
        Keys.Left => InputKey.Left,
        Keys.Right => InputKey.Right,
        Keys.Up => InputKey.Up,
        Keys.Down => InputKey.Down,
        Keys.Home => InputKey.Home,
        Keys.End => InputKey.End,
        Keys.PageUp => InputKey.PageUp,
        Keys.PageDown => InputKey.PageDown,
        Keys.A => InputKey.A,
        Keys.B => InputKey.B,
        Keys.C => InputKey.C,
        Keys.D => InputKey.D,
        Keys.E => InputKey.E,
        Keys.F => InputKey.F,
        Keys.G => InputKey.G,
        Keys.H => InputKey.H,
        Keys.I => InputKey.I,
        Keys.J => InputKey.J,
        Keys.K => InputKey.K,
        Keys.L => InputKey.L,
        Keys.M => InputKey.M,
        Keys.N => InputKey.N,
        Keys.O => InputKey.O,
        Keys.P => InputKey.P,
        Keys.Q => InputKey.Q,
        Keys.R => InputKey.R,
        Keys.S => InputKey.S,
        Keys.T => InputKey.T,
        Keys.U => InputKey.U,
        Keys.V => InputKey.V,
        Keys.W => InputKey.W,
        Keys.X => InputKey.X,
        Keys.Y => InputKey.Y,
        Keys.Z => InputKey.Z,
        _ => InputKey.Unknown,
    };

    private static bool IsWithin(VisualElement? ancestor, VisualElement? element)
    {
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}
