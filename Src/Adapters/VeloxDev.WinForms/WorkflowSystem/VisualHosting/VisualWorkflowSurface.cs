using System.Collections.Specialized;
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.StandardEx;
using DrawingPointF = System.Drawing.PointF;
using DrawingRectangleF = System.Drawing.RectangleF;
using DrawingSize = System.Drawing.Size;
using Wf = VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.VisualHosting;

/// <summary>
/// A reusable workflow surface on the WinForms visual backend: one HWND, visual node cards, a single link layer and
/// the standard input route.
/// </summary>
/// <remarks>
/// <para>
/// The surface owns the composition and the platform half only. It translates the visual backend's pointer, wheel
/// and key arguments into <see cref="WorkflowInput"/> and applies the generic gestures a surface must have: drag a
/// card through <see cref="INodeComponent.MoveCommand"/>, pan the empty canvas, zoom on Ctrl+wheel, delete the
/// hovered link on <c>Delete</c> and open the link/node context menu on a right press. Highlighting colours and the
/// menu rows remain the host's: <see cref="LinkLayer"/> exposes its palette and the two menu builders are supplied
/// by the host.
/// </para>
/// <para>
/// Curves are built and published in canvas-local collapsed model space. Pan is a plain offset applied when cards
/// and curves are painted, never a <see cref="Visual.Transform"/>, so a pointer position is converted to the model
/// by one subtraction and every published curve stays in the space Core hit-tests.
/// </para>
/// </remarks>
public sealed class VisualWorkflowSurface : VisualSurface, IWorkflowSurfaceScroller, IWorkflowSurfaceNavigator
{
    /// <summary>How far one wheel notch scrolls the canvas, in pixels.</summary>
    public const double WheelScrollStep = 60d;

    private readonly WorkflowRoot _root;
    private readonly VisualWorkflowInputRouter _router;
    private ITreeComponent? _tree;
    private WorkflowInput? _input;
    private VisualWorkflowMenu? _openMenu;
    private ILinkComponent? _hoveredLink;
    private bool _panning;
    private DrawingPointF _panLast;
    private ISlotComponent? _connectionSender;
    private DrawingSize _viewport;

    /// <summary>Creates an empty visual workflow surface.</summary>
    public VisualWorkflowSurface()
    {
        BackColor = Color.FromArgb(0xFF, 0x17, 0x17, 0x17);
        _root = new WorkflowRoot(this);
        Root = _root;
        _router = new VisualWorkflowInputRouter(this);
    }

    /// <summary>The tree shown by this surface, or <see langword="null"/>.</summary>
    public ITreeComponent? Tree
    {
        get => _tree;
        set => SetTree(value);
    }

    /// <summary>The canvas that positions node views and owns the link layer.</summary>
    public VisualWorkflowCanvas Canvas => _root.Canvas;

    /// <summary>The single layer that paints every visible link.</summary>
    public VisualWorkflowLinkLayer LinkLayer => _root.Canvas.LinkLayer;

    /// <summary>The grid and axes painted behind the canvas.</summary>
    public VisualWorkflowGridDecorator GridDecorator => _root.Grid;

    /// <summary>The minimap overlay.</summary>
    public VisualWorkflowMinimap Minimap => _root.Minimap;

    /// <summary>Whether the minimap is shown.</summary>
    public bool IsMinimapVisible
    {
        get => Minimap.IsMinimapVisible;
        set
        {
            Minimap.IsMinimapVisible = value;
            InvalidateSurface();
        }
    }

    /// <summary>
    /// Creates a card for a node. The default is a <see cref="VisualWorkflowNodeView"/>; assign
    /// <see cref="NodeViewFactory"/> or derive and override to keep a host's own card design.
    /// </summary>
    public Func<INodeComponent, VisualWorkflowNodeView>? NodeViewFactory { get; set; }

    /// <summary>
    /// Chooses the card for a node before <see cref="NodeViewFactory"/> and the default card are consulted.
    /// </summary>
    /// <remarks>
    /// A selection must be a <see cref="VisualWorkflowNodeView"/> to be placed on the canvas; any other element is
    /// ignored and the next source is tried.
    /// </remarks>
    public VisualTemplateSelector? TemplateSelector { get; set; }

    /// <summary>Fills the context menu opened on a right press over a link.</summary>
    public Action<VisualWorkflowMenu, ILinkComponent>? LinkMenuBuilder { get; set; }

    /// <summary>Fills the context menu opened on a right press over a node.</summary>
    public Action<VisualWorkflowMenu, INodeComponent>? NodeMenuBuilder { get; set; }

    /// <summary>The link the input route currently considers hovered, or <see langword="null"/>.</summary>
    public ILinkComponent? HoveredLink => _input?.HoveredLink;

    /// <summary>Raised when <see cref="HoveredLink"/> changes.</summary>
    public event EventHandler? HoveredLinkChanged;

    internal WorkflowInput? Input => _input;

    /// <summary>Binds a tree, building its views and registering the surface as the input route's scroller and navigator.</summary>
    /// <param name="tree">The tree to show, or <see langword="null"/> to detach.</param>
    public void Attach(ITreeComponent? tree) => SetTree(tree);

    /// <summary>Detaches the current tree, retracts its published curves and clears the route's capabilities.</summary>
    public void Detach() => SetTree(null);

    /// <summary>Re-reads the tree's nodes, re-synchronises geometry and refreshes the frame.</summary>
    public new void Refresh()
    {
        if (_tree is null)
        {
            return;
        }

        Canvas.RefreshFromTree();
        GridDecorator.Pan = Canvas.Pan;
        UpdateViewport();
        InvalidateSurface();
    }

    /// <summary>Re-applies the viewport so the virtualised visible set follows the current pan.</summary>
    public void UpdateViewport()
    {
        if (_tree is null || _viewport.Width <= 0 || _viewport.Height <= 0)
        {
            return;
        }

        try
        {
            _tree.GetHelper().Viewport = new Wf.Viewport(
                -Canvas.Pan.X,
                -Canvas.Pan.Y,
                _viewport.Width,
                _viewport.Height);
        }
        catch
        {
            // A tree helper may not support writing a viewport; the surface still draws and routes.
        }
    }

    /// <summary>Moves logical focus to the surface's root element so key input has a route.</summary>
    public void FocusSurface() => FocusElement(_root);

    /// <summary>Returns the card bound to <paramref name="node"/>, or <see langword="null"/>.</summary>
    /// <param name="node">The node to look up.</param>
    public VisualWorkflowNodeView? FindNodeView(INodeComponent node) => Canvas.FindView(node);

    /// <summary>Zooms around a surface point.</summary>
    /// <param name="surfacePoint">The point kept still, in surface coordinates.</param>
    /// <param name="factor">The scale multiplier; below one zooms out.</param>
    public void ZoomAt(DrawingPointF surfacePoint, double factor)
    {
        Canvas.ZoomAt(surfacePoint, factor);
        GridDecorator.Pan = Canvas.Pan;
        UpdateViewport();
        InvalidateSurface();
    }

    /// <summary>Scrolls the surface as though the wheel had turned.</summary>
    /// <param name="wheelDeltaX">Horizontal wheel movement, positive towards the content on the right.</param>
    /// <param name="wheelDeltaY">Vertical wheel movement, positive towards the content above.</param>
    public void ScrollBy(double wheelDeltaX, double wheelDeltaY)
    {
        SetPan(new DrawingPointF(
            (float)(Canvas.Pan.X + (wheelDeltaX * WheelScrollStep)),
            (float)(Canvas.Pan.Y + (wheelDeltaY * WheelScrollStep))));
    }

    /// <summary>Centres a canvas-local point in the viewport.</summary>
    /// <param name="localX">Horizontal canvas-local coordinate.</param>
    /// <param name="localY">Vertical canvas-local coordinate.</param>
    public void CenterOn(double localX, double localY)
    {
        if (_viewport.Width <= 0 || _viewport.Height <= 0)
        {
            return;
        }

        SetPan(new DrawingPointF(
            (float)((_viewport.Width / 2d) - localX),
            (float)((_viewport.Height / 2d) - localY)));
    }

    /// <inheritdoc />
    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _router.RouteExit();
        SyncHover();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            CloseMenu();
            SetTree(null);
        }

        base.Dispose(disposing);
    }

    internal VisualWorkflowNodeView CreateNodeView(INodeComponent node)
        => TemplateSelector?.SelectTemplate(node) as VisualWorkflowNodeView
           ?? NodeViewFactory?.Invoke(node)
           ?? CreateDefaultNodeView(node);

    internal void OnCanvasGeometryChanged()
    {
        GridDecorator.Pan = Canvas.Pan;
        Minimap.UpdateFrame(_tree, Canvas.Pan, _viewport, this);
        UpdateViewport();
        InvalidateSurface();
    }

    internal void OnRootArranged(DrawingSize size)
    {
        _viewport = size;
        GridDecorator.Pan = Canvas.Pan;
        Minimap.UpdateFrame(_tree, Canvas.Pan, size, this);
        UpdateViewport();
    }

    internal void HandlePointerPressed(VisualPointerEventArgs args)
    {
        var result = _router.RoutePressed(args);
        SyncHover();
        FocusSurface();

        if (args.Button == MouseButtons.Right)
        {
            if (result.Hit.Node is { } node && NodeMenuBuilder is not null)
            {
                OpenNodeMenu(args.Position, node);
            }
            else if (result.Hit.Link is { } link)
            {
                OpenLinkMenu(args.Position, link);
            }

            return;
        }

        if (args.Button == MouseButtons.Left
            && !result.Handle.PreventDefault
            && result.Hit.Slot is { } slot
            && slot.StandardCanBeSender()
            && slot.SendConnectionCommand.CanExecute(null))
        {
            slot.SendConnectionCommand.Execute(null);
            _connectionSender = slot;
            UpdateConnection(args.Position);
            args.Handled = true;
            return;
        }

        if (args.Button == MouseButtons.Left
            && !result.Handle.PreventDefault
            && result.Hit.InCanvas
            && result.Hit.Target is null)
        {
            _panning = true;
            _panLast = args.Position;
        }
    }

    internal void HandlePointerMoved(VisualPointerEventArgs args)
    {
        _router.RouteMoved(args);
        SyncHover();

        if (_connectionSender is not null)
        {
            if ((args.Buttons & MouseButtons.Left) == 0)
            {
                CancelConnection();
                return;
            }

            UpdateConnection(args.Position);
            args.Handled = true;
            return;
        }

        if (!_panning || (args.Buttons & MouseButtons.Left) == 0)
        {
            _panning = false;
            return;
        }

        var deltaX = args.Position.X - _panLast.X;
        var deltaY = args.Position.Y - _panLast.Y;
        if (Math.Abs(deltaX) < 0.01f && Math.Abs(deltaY) < 0.01f)
        {
            return;
        }

        _panLast = args.Position;
        SetPan(new DrawingPointF(Canvas.Pan.X + deltaX, Canvas.Pan.Y + deltaY));
    }

    internal void HandlePointerReleased(VisualPointerEventArgs args)
    {
        _router.RouteReleased(args);
        SyncHover();
        _panning = false;

        if (_connectionSender is { } sender)
        {
            _connectionSender = null;
            if (_tree is { } tree)
            {
                var model = Canvas.SurfaceToModel(args.Position);
                var target = SlotHitTestEx.HitTest(tree, model.Horizontal, model.Vertical);
                if (target is not null
                    && !ReferenceEquals(target, sender)
                    && target.ReceiveConnectionCommand.CanExecute(null))
                {
                    target.ReceiveConnectionCommand.Execute(null);
                }
                else
                {
                    tree.ResetVirtualLinkCommand.Execute(null);
                }

                LinkLayer.Refresh();
                InvalidateSurface();
            }

            args.Handled = true;
        }
    }

    internal void HandlePointerWheel(VisualPointerEventArgs args)
    {
        var result = _router.RouteWheel(args);
        SyncHover();
        if (result.Handle.PreventDefault)
        {
            args.Handled = true;
            return;
        }

        var delta = args.Delta / 120d;
        if ((args.Modifiers & Keys.Control) != 0)
        {
            ZoomAt(args.Position, args.Delta > 0 ? 1d / 1.1d : 1.1d);
        }
        else if ((args.Modifiers & Keys.Shift) != 0)
        {
            // 横轴正 = 向右；滚轮向上（正）在画面上是向左，所以取反。
            ScrollBy(-delta, 0d);
        }
        else
        {
            ScrollBy(0d, delta);
        }

        args.Handled = true;
    }

    internal void HandleKeyDown(VisualKeyEventArgs args)
    {
        var result = _router.RouteKeyDown(args);
        if (result.Handle.PreventDefault)
        {
            args.Handled = true;
            return;
        }

        var key = args.KeyCode & Keys.KeyCode;
        if (key == Keys.Delete && _input?.HoveredLink is { } hovered)
        {
            if (hovered.DeleteCommand.CanExecute(null))
            {
                hovered.DeleteCommand.Execute(null);
            }

            _router.RouteExit();
            SyncHover();
            args.Handled = true;
            return;
        }

        if (key == Keys.Escape && _connectionSender is not null)
        {
            CancelConnection();
            args.Handled = true;
            return;
        }

        if ((args.Modifiers & Keys.Control) == 0 || _input is not { } input)
        {
            return;
        }

        var redo = (args.Modifiers & Keys.Shift) != 0;
        if (key == Keys.Z && !redo)
        {
            input.Tree.UndoCommand.Execute(null);
            args.Handled = true;
        }
        else if (key == Keys.Y || (key == Keys.Z && redo))
        {
            input.Tree.RedoCommand.Execute(null);
            args.Handled = true;
        }
    }

    internal void HandleKeyUp(VisualKeyEventArgs args) => _router.RouteKeyUp(args);

    private VisualWorkflowNodeView CreateDefaultNodeView(INodeComponent node) => new();

    private void SetTree(ITreeComponent? tree)
    {
        if (ReferenceEquals(_tree, tree))
        {
            return;
        }

        CancelConnection();

        if (_tree is { } previous)
        {
            _router.RouteExit();
            var input = WorkflowInput.For(previous);
            if (ReferenceEquals(input.Scroller, this))
            {
                input.Scroller = null;
            }

            if (ReferenceEquals(input.Navigator, this))
            {
                input.Navigator = null;
            }

            input.IsSuspended = false;
        }

        Canvas.Detach();
        CloseMenu();
        _input = null;
        _tree = null;
        _hoveredLink = null;
        LinkLayer.Hovered = null;

        if (tree is null)
        {
            InvalidateSurface();
            return;
        }

        _tree = tree;
        Canvas.Attach(tree);
        _input = WorkflowInput.For(tree);
        _input.HitRadius = _router.HitRadius;
        _input.Scroller = this;
        _input.Navigator = this;
        GridDecorator.Pan = Canvas.Pan;
        Refresh();
    }

    private void UpdateConnection(DrawingPointF surfacePoint)
    {
        if (_connectionSender is null || _tree is not { } tree)
        {
            return;
        }

        var model = Canvas.SurfaceToModel(surfacePoint);
        if (tree.SetPointerCommand.CanExecute(model))
        {
            tree.SetPointerCommand.Execute(model);
        }

        tree.StandardSetPreviewReceiver(SlotHitTestEx.HitTest(tree, model.Horizontal, model.Vertical));
        LinkLayer.Refresh();
        InvalidateSurface();
    }

    private void CancelConnection()
    {
        if (_connectionSender is null)
        {
            return;
        }

        _connectionSender = null;
        _tree?.ResetVirtualLinkCommand.Execute(null);
        LinkLayer.Refresh();
        InvalidateSurface();
    }

    private void SetPan(DrawingPointF pan)
    {
        Canvas.SetPan(pan);
        GridDecorator.Pan = pan;
        Minimap.UpdateFrame(_tree, pan, _viewport, this);
        UpdateViewport();
        InvalidateSurface();
    }

    private void SyncHover()
    {
        var next = _input?.HoveredLink;
        LinkLayer.Hovered = next;
        if (ReferenceEquals(next, _hoveredLink))
        {
            return;
        }

        _hoveredLink = next;
        HoveredLinkChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OpenLinkMenu(DrawingPointF surfacePoint, ILinkComponent link)
    {
        var menu = new VisualWorkflowMenu();
        AttachMenu(menu);
        menu.OpenForLink(_root, surfacePoint, link, LinkMenuBuilder);
    }

    private void OpenNodeMenu(DrawingPointF surfacePoint, INodeComponent node)
    {
        var menu = new VisualWorkflowMenu();
        AttachMenu(menu);
        if (!menu.OpenForNode(_root, surfacePoint, node, NodeMenuBuilder))
        {
            _openMenu = null;
            if (_input is not null)
            {
                _input.IsSuspended = false;
            }
        }
    }

    private void AttachMenu(VisualWorkflowMenu menu)
    {
        CloseMenu();
        _openMenu = menu;
        if (_input is not null)
        {
            _input.IsSuspended = true;
        }

        menu.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_openMenu, menu))
            {
                return;
            }

            _openMenu = null;
            if (_input is not null)
            {
                _input.IsSuspended = false;
            }
        };
    }

    private void CloseMenu()
    {
        var menu = _openMenu;
        _openMenu = null;
        if (menu is null)
        {
            return;
        }

        if (menu.IsOpen)
        {
            menu.Close();
        }

        menu.Dispose();
    }

    // 根元素铺满整个表面，是 grid / canvas / minimap 的容器，也是指针与键盘事件冒泡到的最后一站。
    private sealed class WorkflowRoot : VisualElement
    {
        private readonly VisualWorkflowSurface _owner;

        internal WorkflowRoot(VisualWorkflowSurface owner)
        {
            _owner = owner;
            IsHitTestVisible = false;
            Focusable = true;
            Grid = new VisualWorkflowGridDecorator();
            Canvas = new VisualWorkflowCanvas(owner);
            Minimap = new VisualWorkflowMinimap();
            Add(Grid);
            Add(Canvas);
            Add(Minimap);
        }

        internal VisualWorkflowGridDecorator Grid { get; }

        internal VisualWorkflowCanvas Canvas { get; }

        internal VisualWorkflowMinimap Minimap { get; }

        protected override DrawingSize MeasureOverride(DrawingSize availableSize)
        {
            Grid.Measure(availableSize);
            Canvas.Measure(availableSize);
            Minimap.Measure(new DrawingSize(
                (int)Math.Ceiling(Minimap.Width),
                (int)Math.Ceiling(Minimap.Height)));
            return availableSize;
        }

        protected override void ArrangeOverride(DrawingSize finalSize)
        {
            Grid.Arrange(new DrawingRectangleF(0f, 0f, finalSize.Width, finalSize.Height));
            Canvas.Arrange(new DrawingRectangleF(0f, 0f, finalSize.Width, finalSize.Height));

            if (Minimap.IsVisible)
            {
                var width = (float)Minimap.Width;
                var height = (float)Minimap.Height;
                var x = Math.Max(8f, finalSize.Width - width - 12f);
                Minimap.Arrange(new DrawingRectangleF(x, 12f, width, height));
            }
            else
            {
                Minimap.Arrange(new DrawingRectangleF(0f, 0f, 0f, 0f));
            }

            _owner.OnRootArranged(finalSize);
        }

        protected override void OnPointerPressed(VisualPointerEventArgs args)
        {
            base.OnPointerPressed(args);
            _owner.HandlePointerPressed(args);
        }

        protected override void OnPointerMoved(VisualPointerEventArgs args)
        {
            base.OnPointerMoved(args);
            _owner.HandlePointerMoved(args);
        }

        protected override void OnPointerReleased(VisualPointerEventArgs args)
        {
            base.OnPointerReleased(args);
            _owner.HandlePointerReleased(args);
        }

        protected override void OnPointerWheel(VisualPointerEventArgs args)
        {
            base.OnPointerWheel(args);
            _owner.HandlePointerWheel(args);
        }

        protected override void OnKeyDown(VisualKeyEventArgs args)
        {
            base.OnKeyDown(args);
            _owner.HandleKeyDown(args);
        }

        protected override void OnKeyUp(VisualKeyEventArgs args)
        {
            base.OnKeyUp(args);
            _owner.HandleKeyUp(args);
        }
    }
}
