using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using VeloxDev.WinForms.Xaml;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using Wf = VeloxDev.WorkflowSystem;
using VisualBorder = VeloxDev.WinForms.Xaml.Visuals.Controls.Border;
using VisualCanvas = VeloxDev.WinForms.Xaml.Visuals.Controls.Canvas;
using VisualStackPanel = VeloxDev.WinForms.Xaml.Visuals.Controls.StackPanel;
using VisualTextBlock = VeloxDev.WinForms.Xaml.Visuals.Controls.TextBlock;
using XamlThickness = VeloxDev.WinForms.Xaml.Controls.Thickness;
using WfSize = VeloxDev.WorkflowSystem.Size;
using Size = System.Drawing.Size;

namespace Demo.Views;

/// <summary>
/// A demo-only workflow surface on the visual backend: a real <see cref="TreeDefaultComponent"/> model, visual
/// node cards, a single link layer that publishes the curves it draws, and demo gestures (drag, pan, Ctrl+wheel
/// zoom, hover highlight, Delete).
/// </summary>
/// <remarks>
/// Appearance and policy live here, not in the adapter or the item templates: the surface routes standard input
/// through <see cref="WorkflowInput"/> and then applies its own gesture. Curves are built and published in model
/// (collapsed) space and only offset by the pan when drawn, so hit testing stays in the space the model exposes.
/// </remarks>
internal sealed class WorkflowGraphSample : VisualCanvas
{
    private const double NodeWidth = 190d;
    private const double NodeHeight = 78d;

    private readonly TreeDefaultComponent _tree = new();
    private readonly LinkLayer _links;
    private readonly Dictionary<INodeComponent, VisualBorder> _nodeVisuals = [];
    private readonly Dictionary<VisualElement, INodeComponent> _visualNodes = [];
    private PointF _pan;
    private PointF _lastPointer;
    private INodeComponent? _dragNode;
    private bool _panning;
    private ILinkComponent? _hovered;

    /// <summary>Builds the sample model and its visual cards.</summary>
    public WorkflowGraphSample()
    {
        Width = 760f;
        Height = 320f;
        IsHitTestVisible = true;
        Focusable = true;
        Background = Theme.Current.SurfaceAlt;
        CornerRadius = 8f;
        Padding = new XamlThickness(0f);

        _tree.Layout.OriginSize = new WfSize(1100d, 700d);
        _links = new LinkLayer(this, _tree) { Width = Width, Height = Height };

        var prompt = AddNode("Prompt", "Workflow input", 20d, 40d);
        var planner = AddNode("Planner", "Route the request", 290d, 20d);
        var tools = AddNode("Tools", "Call a capability", 290d, 200d);
        var output = AddNode("Output", "Emit the result", 560d, 110d);

        AddLink(prompt, planner);
        AddLink(prompt, tools);
        AddLink(planner, output);
        AddLink(tools, output);

        Add(_links);
        foreach (var pair in _nodeVisuals)
        {
            Add(pair.Value);
        }

        _tree.Links.CollectionChanged += OnLinksChanged;
        PositionAll();
        _links.Refresh();
    }

    /// <summary>The model tree shown by this sample.</summary>
    internal TreeDefaultComponent Tree => _tree;

    /// <summary>Returns a node card's rectangle in surface coordinates; used by capture automation.</summary>
    /// <param name="node">The node to locate.</param>
    internal RectangleF NodeBounds(INodeComponent node)
        => _nodeVisuals.TryGetValue(node, out var card) ? card.AbsoluteBounds : RectangleF.Empty;

    /// <summary>A point on the first link's drawn curve, in surface coordinates; used by capture automation.</summary>
    internal PointF FirstLinkMidpoint()
    {
        if (_tree.Links.Count == 0
            || _tree.Links[0].Sender?.Parent is not { } sender
            || _tree.Links[0].Receiver?.Parent is not { } receiver)
        {
            return PointF.Empty;
        }

        var startX = sender.Anchor.Horizontal + sender.Size.Width;
        var startY = sender.Anchor.Vertical + (sender.Size.Height / 2d);
        var endX = receiver.Anchor.Horizontal;
        var endY = receiver.Anchor.Vertical + (receiver.Size.Height / 2d);
        var points = LinkCurve.LinkCurvePoints(_tree.Links[0], startX, startY, endX, endY, 40d);
        var x = (points[0].X + (3 * points[1].X) + (3 * points[2].X) + points[3].X) / 8d;
        var y = (points[0].Y + (3 * points[1].Y) + (3 * points[2].Y) + points[3].Y) / 8d;
        var origin = AbsoluteBounds.Location;
        return new PointF((float)(x + _pan.X + origin.X), (float)(y + _pan.Y + origin.Y));
    }

    /// <summary>Zooms around a fixed point; used by capture automation to prove the layout collapse.</summary>
    internal void ZoomDemo()
    {
        var origin = AbsoluteBounds.Location;
        ZoomAt(new PointF(origin.X + 380f, origin.Y + 160f), 1d / 1.3d);
    }

    /// <inheritdoc />
    protected override void ArrangeOverride(Size finalSize)
    {
        // 画布是这一节的边界：平移后的节点和连线不能画到相邻章节上。
        Clip = new RectangleF(0f, 0f, finalSize.Width, finalSize.Height);
        base.ArrangeOverride(finalSize);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(VisualPointerEventArgs args)
    {
        base.OnPointerPressed(args);
        Focus();

        var node = FindNode(Surface?.FindElementAt(args.Position));
        var target = (IWorkflowComponent?)node ?? HitTestLink(ModelPoint(args.Position));
        var handle = new WorkflowEventHandle();
        WorkflowInput.For(_tree).Route(new Wf.PointerPressedEventArgs(
            ModelPoint(args.Position),
            ToModifiers(args.Modifiers),
            this,
            target,
            ToButton(args.Button),
            args.ClickCount,
            handle));

        if (handle.PreventDefault || args.Button != MouseButtons.Left)
        {
            return;
        }

        _lastPointer = args.Position;
        if (node is not null)
        {
            _dragNode = node;
        }
        else
        {
            _panning = true;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(VisualPointerEventArgs args)
    {
        base.OnPointerMoved(args);
        var model = ModelPoint(args.Position);

        if (_dragNode is not null)
        {
            var delta = new PointF(args.Position.X - _lastPointer.X, args.Position.Y - _lastPointer.Y);
            var scale = _tree.Layout.Scale.Horizontal;
            _dragNode.MoveCommand.Execute(new Offset(delta.X * scale, delta.Y * scale));
            _lastPointer = args.Position;
        }
        else if (_panning)
        {
            _pan = new PointF(_pan.X + args.Position.X - _lastPointer.X, _pan.Y + args.Position.Y - _lastPointer.Y);
            _lastPointer = args.Position;
            PositionAll();
            _links.InvalidateVisual();
        }

        var link = HitTestLink(model);
        if (!ReferenceEquals(link, _hovered))
        {
            _hovered = link;
            _links.Hovered = link;
            _links.InvalidateVisual();
        }

        var node = FindNode(Surface?.FindElementAt(args.Position));
        WorkflowInput.For(_tree).Route(new Wf.PointerMovedEventArgs(
            model,
            ToModifiers(args.Modifiers),
            this,
            (IWorkflowComponent?)node ?? link,
            new WorkflowEventHandle()));
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(VisualPointerEventArgs args)
    {
        base.OnPointerReleased(args);
        _dragNode = null;
        _panning = false;

        var node = FindNode(Surface?.FindElementAt(args.Position));
        WorkflowInput.For(_tree).Route(new Wf.PointerReleasedEventArgs(
            ModelPoint(args.Position),
            ToModifiers(args.Modifiers),
            this,
            (IWorkflowComponent?)node ?? HitTestLink(ModelPoint(args.Position)),
            ToButton(args.Button),
            1,
            new WorkflowEventHandle()));
    }

    /// <inheritdoc />
    protected override void OnPointerWheel(VisualPointerEventArgs args)
    {
        base.OnPointerWheel(args);

        var handle = new WorkflowEventHandle();
        WorkflowInput.For(_tree).Route(new Wf.PointerWheelEventArgs(
            ModelPoint(args.Position),
            ToModifiers(args.Modifiers),
            this,
            HitTestLink(ModelPoint(args.Position)),
            0d,
            args.Delta / 120d,
            handle));

        if (handle.PreventDefault || (args.Modifiers & Keys.Control) == 0 || args.Delta == 0)
        {
            return;
        }

        ZoomAt(args.Position, args.Delta > 0 ? 1d / 1.1d : 1.1d);
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerExited(VisualPointerEventArgs args)
    {
        base.OnPointerExited(args);
        if (_hovered is not null)
        {
            _hovered = null;
            _links.Hovered = null;
            _links.InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(VisualKeyEventArgs args)
    {
        base.OnKeyDown(args);
        if (args.KeyCode != Keys.Delete)
        {
            return;
        }

        var handle = new WorkflowEventHandle();
        WorkflowInput.For(_tree).Route(new KeyDownEventArgs(
            InputKey.Delete,
            (int)Keys.Delete,
            ToModifiers(args.Modifiers),
            args.IsRepeat,
            this,
            _hovered,
            handle));

        if (handle.PreventDefault || _hovered is null || !_hovered.DeleteCommand.CanExecute(null))
        {
            return;
        }

        _hovered.DeleteCommand.Execute(null);
        _hovered = null;
        _links.Hovered = null;
        _links.Refresh();
        args.Handled = true;
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tree.Links.CollectionChanged -= OnLinksChanged;
        }

        base.Dispose(disposing);
    }

    private INodeComponent AddNode(string title, string body, double left, double top)
    {
        var slot = new SlotDefaultComponent { Channel = SlotChannel.MultipleBoth };
        var node = new NodeDefaultComponent
        {
            Parent = _tree,
            Anchor = new Anchor(left, top, 0),
            Size = new WfSize(NodeWidth, NodeHeight),
        };
        slot.Parent = node;
        node.Slots.Add(slot);
        _tree.Nodes.Add(node);

        var stack = new VisualStackPanel { Spacing = 4 };
        stack.Add(new VisualTextBlock
        {
            Text = title,
            Font = new Font("Segoe UI Semibold", 11f),
            Foreground = Theme.Current.Accent,
        });
        stack.Add(new VisualTextBlock
        {
            Text = body,
            Foreground = Theme.Current.MutedForeground,
        });

        var card = new VisualBorder
        {
            Width = NodeWidth,
            Height = NodeHeight,
            IsHitTestVisible = true,
            Background = Theme.Current.Surface,
            BorderBrush = Theme.Current.ControlBorder,
            BorderThickness = new XamlThickness(1f),
            CornerRadius = 8f,
            Padding = new XamlThickness(12f, 10f),
            Child = stack,
            DataContext = node,
        };

        node.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(INodeComponent.Anchor) or nameof(INodeComponent.Size))
            {
                Position(node);
                _links.Refresh();
            }
        };

        _nodeVisuals[node] = card;
        _visualNodes[card] = node;
        return node;
    }

    private void AddLink(INodeComponent sender, INodeComponent receiver)
    {
        var senderSlot = sender.Slots[0];
        var receiverSlot = receiver.Slots[0];
        var link = new LinkDefaultComponent
        {
            Sender = senderSlot,
            Receiver = receiverSlot,
            IsVisible = true,
        };

        // 直接往 Links 里加不会建立 LinksMap / Targets / Sources；删除命令靠这张表判断这条线是否在树里。
        senderSlot.Targets.Add(receiverSlot);
        receiverSlot.Sources.Add(senderSlot);
        if (!_tree.LinksMap.TryGetValue(senderSlot, out var receivers))
        {
            receivers = [];
            _tree.LinksMap[senderSlot] = receivers;
        }

        receivers[receiverSlot] = link;
        _tree.Links.Add(link);
    }

    private INodeComponent? FindNode(VisualElement? element)
    {
        for (var current = element; current is not null; current = current.ParentElement)
        {
            if (_visualNodes.TryGetValue(current, out var node))
            {
                return node;
            }
        }

        return null;
    }

    private ILinkComponent? HitTestLink(Anchor modelPoint)
    {
        for (var index = _tree.Links.Count - 1; index >= 0; index--)
        {
            var link = _tree.Links[index];
            if (link.HitTest(modelPoint.Horizontal, modelPoint.Vertical))
            {
                return link;
            }
        }

        return null;
    }

    private void PositionAll()
    {
        foreach (var node in _nodeVisuals.Keys)
        {
            Position(node);
        }
    }

    private void Position(INodeComponent node)
    {
        if (!_nodeVisuals.TryGetValue(node, out var card))
        {
            return;
        }

        var anchor = node.Anchor;
        var size = node.Size;
        VisualCanvas.SetLeft(card, (float)(anchor.Horizontal + _pan.X));
        VisualCanvas.SetTop(card, (float)(anchor.Vertical + _pan.Y));
        card.Width = Math.Max(40d, size.Width);
        card.Height = Math.Max(28d, size.Height);
    }

    // 缩放保持指针下的世界点不动：屏幕 = 坍缩坐标 + 平移，坍缩坐标 = 世界 / Scale。
    private void ZoomAt(PointF screen, double factor)
    {
        var current = _tree.Layout.Scale.Horizontal;
        var next = Math.Clamp(current * factor, 0.25d, 4d);
        if (Math.Abs(next - current) < 0.0001d)
        {
            return;
        }

        var origin = AbsoluteBounds.Location;
        var localX = screen.X - origin.X;
        var localY = screen.Y - origin.Y;
        var worldX = (localX - _pan.X) * current;
        var worldY = (localY - _pan.Y) * current;
        _tree.Layout.Scale = new Scale(next, next);
        _pan = new PointF(
            (float)(localX - (worldX / next)),
            (float)(localY - (worldY / next)));

        PositionAll();
        _links.Refresh();
    }

    // 输入坐标是 surface 坐标；模型/绘制都用画布局部坐标，所以先减掉画布自己在 surface 里的原点。
    private Anchor ModelPoint(PointF screen)
    {
        var origin = AbsoluteBounds.Location;
        return new Anchor(screen.X - origin.X - _pan.X, screen.Y - origin.Y - _pan.Y, 0);
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _hovered = null;
        _links.Hovered = null;
        _links.Refresh();
    }

    private static InputModifiers ToModifiers(Keys keys)
    {
        var modifiers = InputModifiers.None;
        if ((keys & Keys.Control) != 0) modifiers |= InputModifiers.Control;
        if ((keys & Keys.Shift) != 0) modifiers |= InputModifiers.Shift;
        if ((keys & Keys.Alt) != 0) modifiers |= InputModifiers.Alt;
        return modifiers;
    }

    private static MouseButton ToButton(MouseButtons button) => button switch
    {
        MouseButtons.Left => MouseButton.Left,
        MouseButtons.Right => MouseButton.Right,
        MouseButtons.Middle => MouseButton.Middle,
        _ => MouseButton.None,
    };

    // 单层连线：一趟画出所有可见线，并把画出来的曲线按模型空间发布给 Core 命中判定。
    private sealed class LinkLayer : VisualElement
    {
        private readonly WorkflowGraphSample _owner;
        private readonly ITreeComponent _tree;
        private readonly List<(ILinkComponent Link, (double X, double Y)[] Points)> _curves = [];

        internal LinkLayer(WorkflowGraphSample owner, ITreeComponent tree)
        {
            _owner = owner;
            _tree = tree;
            IsHitTestVisible = false;
        }

        internal ILinkComponent? Hovered { get; set; }

        internal void Refresh()
        {
            _curves.Clear();
            foreach (var link in _tree.Links)
            {
                if (!link.IsVisible || link.Sender?.Parent is not { } sender || link.Receiver?.Parent is not { } receiver)
                {
                    link.PublishCurve(null);
                    continue;
                }

                var startX = sender.Anchor.Horizontal + sender.Size.Width;
                var startY = sender.Anchor.Vertical + (sender.Size.Height / 2d);
                var endX = receiver.Anchor.Horizontal;
                var endY = receiver.Anchor.Vertical + (receiver.Size.Height / 2d);

                _curves.Add((link, LinkCurve.LinkCurvePoints(link, startX, startY, endX, endY, 40d)));
                link.PublishCurve(LinkCurve.BuildLinkCubic(link, startX, startY, endX, endY, 40d));
            }

            InvalidateVisual();
        }

        protected override void OnRender(Graphics graphics)
        {
            var theme = Theme.Current;
            foreach (var (link, points) in _curves)
            {
                var hovered = ReferenceEquals(link, Hovered);
                using var pen = new System.Drawing.Pen(hovered ? theme.FocusRing : theme.Accent, hovered ? 2.6f : 1.8f)
                {
                    StartCap = LineCap.Round,
                    EndCap = LineCap.Round,
                };

                var pan = _owner._pan;
                graphics.DrawBezier(
                    pen,
                    (float)points[0].X + pan.X,
                    (float)points[0].Y + pan.Y,
                    (float)points[1].X + pan.X,
                    (float)points[1].Y + pan.Y,
                    (float)points[2].X + pan.X,
                    (float)points[2].Y + pan.Y,
                    (float)points[3].X + pan.X,
                    (float)points[3].Y + pan.Y);
            }
        }
    }
}
