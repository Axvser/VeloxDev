using Demo.ViewModels;
using Demo.Workflow;
using System.Collections.Specialized;
using System.ComponentModel;
using VeloxDev.WorkflowSystem;
using WFSize = System.Drawing.Size;

namespace Demo.Controls;

public sealed class WorkflowSurfaceControl : Panel
{
    private readonly Dictionary<IWorkflowNodeViewModel, WorkflowNodeControl> _cards = [];
    private WorkflowDemoSession? _session;

    internal bool IsConnectionActive => _session?.Tree.VirtualLink.IsVisible == true;

    public WorkflowSurfaceControl()
    {
        AutoScroll = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(15, 23, 42);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public WorkflowDemoSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value))
            {
                return;
            }

            DetachSession(_session);
            _session = value;
            AttachSession(value);
            Rebuild();
        }
    }

    private void AttachSession(WorkflowDemoSession? session)
    {
        if (session is null)
        {
            return;
        }

        session.Tree.Nodes.CollectionChanged += OnNodesChanged;
        session.Tree.Links.CollectionChanged += OnLinksChanged;
        session.Controller.PropertyChanged += OnControllerPropertyChanged;

        foreach (var node in session.Tree.Nodes)
        {
            SubscribeNode(node);
        }
    }

    private void DetachSession(WorkflowDemoSession? session)
    {
        if (session is null)
        {
            return;
        }

        session.Tree.Nodes.CollectionChanged -= OnNodesChanged;
        session.Tree.Links.CollectionChanged -= OnLinksChanged;
        session.Controller.PropertyChanged -= OnControllerPropertyChanged;

        foreach (var node in session.Tree.Nodes)
        {
            UnsubscribeNode(node);
        }

        foreach (var card in _cards.Values)
        {
            card.Disconnect();
            Controls.Remove(card);
            card.Dispose();
        }

        _cards.Clear();
    }

    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var node in e.OldItems.OfType<IWorkflowNodeViewModel>())
            {
                UnsubscribeNode(node);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var node in e.NewItems.OfType<IWorkflowNodeViewModel>())
            {
                SubscribeNode(node);
            }
        }

        BeginInvoke(new Action(Rebuild));
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => BeginInvoke(new Action(() => Invalidate()));

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ControllerViewModel.IsActive))
        {
            BeginInvoke(new Action(RefreshCards));
        }
    }

    private void SubscribeNode(IWorkflowNodeViewModel node)
    {
        if (node is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged += OnNodePropertyChanged;
        }
    }

    private void UnsubscribeNode(IWorkflowNodeViewModel node)
    {
        if (node is INotifyPropertyChanged notify)
        {
            notify.PropertyChanged -= OnNodePropertyChanged;
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new PropertyChangedEventHandler(OnNodePropertyChanged), sender, e);
            return;
        }

        if (sender is not IWorkflowNodeViewModel node || !_cards.TryGetValue(node, out var card))
        {
            return;
        }

        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            card.Bounds = CreateBounds(node);
            SyncNodeSlotAnchors(node);
            UpdateCanvasSize();
            Invalidate();
            return;
        }

        card.RefreshFromViewModel();
        RefreshCards();
    }

    private void Rebuild()
    {
        SuspendLayout();
        try
        {
            foreach (var existing in _cards.Values)
            {
                existing.Disconnect();
                Controls.Remove(existing);
                existing.Dispose();
            }

            _cards.Clear();

            if (_session is null)
            {
                AutoScrollMinSize = WFSize.Empty;
                return;
            }

            foreach (var node in _session.Tree.Nodes)
            {
                var card = new WorkflowNodeControl(node, IsWorkflowActive)
                {
                    Bounds = CreateBounds(node)
                };
                _cards[node] = card;
                Controls.Add(card);
            }

            SyncAllSlotAnchors();
            UpdateCanvasSize();
            RefreshCards();
        }
        finally
        {
            ResumeLayout();
            Invalidate();
        }
    }

    private Rectangle CreateBounds(IWorkflowNodeViewModel node)
        => new((int)Math.Round(node.Anchor.Horizontal), (int)Math.Round(node.Anchor.Vertical), (int)Math.Round(node.Size.Width), (int)Math.Round(node.Size.Height));

    private void UpdateCanvasSize()
    {
        if (_session is null || _session.Tree.Nodes.Count == 0)
        {
            AutoScrollMinSize = new WFSize(1280, 760);
            return;
        }

        var width = Math.Max(1280, (int)Math.Ceiling(_session.Tree.Nodes.Max(x => x.Anchor.Horizontal + x.Size.Width) + 80));
        var height = Math.Max(760, (int)Math.Ceiling(_session.Tree.Nodes.Max(x => x.Anchor.Vertical + x.Size.Height) + 80));
        AutoScrollMinSize = new WFSize(width, height);
    }

    private void RefreshCards()
    {
        foreach (var card in _cards.Values)
        {
            card.RefreshVisualState();
        }

        Invalidate();
    }

    private bool IsWorkflowActive() => _session?.Controller.IsActive == true;

    internal void BeginConnection(IWorkflowSlotViewModel slot)
    {
        if (_session is null)
        {
            return;
        }

        slot.SendConnectionCommand.Execute(null);
        Invalidate();
    }

    internal void UpdateConnection(Point clientPoint)
    {
        if (_session is null || !IsConnectionActive)
        {
            return;
        }

        _session.Tree.SetPointerCommand.Execute(ToCanvasAnchor(clientPoint));
        Invalidate();
    }

    internal void EndConnection(Point clientPoint, IWorkflowSlotViewModel? sourceSlot = null)
    {
        if (_session is null)
        {
            return;
        }

        var receiver = HitTestSlot(ToCanvasAnchor(clientPoint), sourceSlot);
        if (receiver is not null)
        {
            receiver.ReceiveConnectionCommand.Execute(null);
        }
        else
        {
            _session.Tree.ResetVirtualLinkCommand.Execute(null);
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        DrawGrid(e.Graphics);

        if (_session is null)
        {
            return;
        }

        using var pen = new Pen(Color.FromArgb(34, 211, 238), 4f);
        using var endpointBrush = new SolidBrush(Color.FromArgb(103, 232, 249));

        foreach (var link in _session.Tree.Links.Where(x => x.IsVisible))
        {
            DrawLink(e.Graphics, pen, endpointBrush, link);
        }

        if (_session.Tree.VirtualLink.IsVisible)
        {
            using var virtualPen = new Pen(Color.FromArgb(226, 232, 240), 3f);
            using var virtualBrush = new SolidBrush(Color.FromArgb(226, 232, 240));
            DrawLink(e.Graphics, virtualPen, virtualBrush, _session.Tree.VirtualLink);
        }

        using var slotPen = new Pen(Color.White, 1.5f);
        foreach (var node in _session.Tree.Nodes)
        {
            DrawSlot(e.Graphics, slotPen, (node as NodeViewModel)?.InputSlot);
            DrawSlot(e.Graphics, slotPen, (node as NodeViewModel)?.OutputSlot);
            DrawSlot(e.Graphics, slotPen, (node as ControllerViewModel)?.OutputSlot);
        }
    }

    private void SyncAllSlotAnchors()
    {
        if (_session is null)
        {
            return;
        }

        foreach (var node in _session.Tree.Nodes)
        {
            SyncNodeSlotAnchors(node);
        }
    }

    private static void SyncNodeSlotAnchors(IWorkflowNodeViewModel node)
    {
        if (node is NodeViewModel workflowNode)
        {
            SyncSlotAnchor(node, workflowNode.InputSlot, isInput: true);
            SyncSlotAnchor(node, workflowNode.OutputSlot, isInput: false);
            return;
        }

        if (node is ControllerViewModel controller)
        {
            SyncSlotAnchor(node, controller.OutputSlot, isInput: false);
        }
    }

    private static void SyncSlotAnchor(IWorkflowNodeViewModel node, IWorkflowSlotViewModel? slot, bool isInput)
    {
        if (slot is null)
        {
            return;
        }

        slot.Anchor = new Anchor(
            node.Anchor.Horizontal + (isInput ? 0 : node.Size.Width),
            node.Anchor.Vertical + (node.Size.Height / 2),
            slot.Anchor.Layer);
    }

    private static void DrawSlot(Graphics graphics, Pen pen, IWorkflowSlotViewModel? slot)
    {
        if (slot is null)
        {
            return;
        }

        using var brush = new SolidBrush(ResolveSlotColor(slot.State));
        var x = (float)slot.Anchor.Horizontal - 10;
        var y = (float)slot.Anchor.Vertical - 10;
        graphics.FillEllipse(brush, x, y, 20, 20);
        graphics.DrawEllipse(pen, x, y, 20, 20);
    }

    private static void DrawLink(Graphics graphics, Pen pen, Brush endpointBrush, IWorkflowLinkViewModel link)
    {
        var startX = (float)link.Sender.Anchor.Horizontal;
        var startY = (float)link.Sender.Anchor.Vertical;
        var endX = (float)link.Receiver.Anchor.Horizontal;
        var endY = (float)link.Receiver.Anchor.Vertical;
        var controlOffset = Math.Max(80f, Math.Abs(endX - startX) * 0.45f);

        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddBezier(startX, startY, startX + controlOffset, startY, endX - controlOffset, endY, endX, endY);
        graphics.DrawPath(pen, path);
        graphics.FillEllipse(endpointBrush, startX - 5, startY - 5, 10, 10);
        graphics.FillEllipse(endpointBrush, endX - 5, endY - 5, 10, 10);
    }

    private Anchor ToCanvasAnchor(Point clientPoint)
        => new(clientPoint.X - AutoScrollPosition.X, clientPoint.Y - AutoScrollPosition.Y, 0);

    private IWorkflowSlotViewModel? HitTestSlot(Anchor anchor, IWorkflowSlotViewModel? exclude = null)
    {
        if (_session is null)
        {
            return null;
        }

        const double radius = 18d;
        var radiusSquared = radius * radius;

        foreach (var slot in EnumerateSlots())
        {
            if (ReferenceEquals(slot, exclude))
            {
                continue;
            }

            var dx = slot.Anchor.Horizontal - anchor.Horizontal;
            var dy = slot.Anchor.Vertical - anchor.Vertical;
            if ((dx * dx) + (dy * dy) <= radiusSquared)
            {
                return slot;
            }
        }

        return null;
    }

    private IEnumerable<IWorkflowSlotViewModel> EnumerateSlots()
    {
        if (_session is null)
        {
            yield break;
        }

        foreach (var node in _session.Tree.Nodes)
        {
            if (node is NodeViewModel workflowNode)
            {
                if (workflowNode.InputSlot is not null)
                {
                    yield return workflowNode.InputSlot;
                }

                if (workflowNode.OutputSlot is not null)
                {
                    yield return workflowNode.OutputSlot;
                }

                continue;
            }

            if (node is ControllerViewModel controller && controller.OutputSlot is not null)
            {
                yield return controller.OutputSlot;
            }
        }
    }

    private static Color ResolveSlotColor(SlotState state)
        => state switch
        {
            var value when value.HasFlag(SlotState.Sender) && value.HasFlag(SlotState.Receiver) => Color.Violet,
            var value when value.HasFlag(SlotState.Sender) => Color.Tomato,
            var value when value.HasFlag(SlotState.Receiver) => Color.Lime,
            _ => Color.White,
        };

    private void DrawGrid(Graphics graphics)
    {
        using var pen = new Pen(Color.FromArgb(30, 41, 59), 1f);
        var width = Math.Max(Width, AutoScrollMinSize.Width);
        var height = Math.Max(Height, AutoScrollMinSize.Height);

        for (var x = 0; x < width; x += 40)
        {
            graphics.DrawLine(pen, x, 0, x, height);
        }

        for (var y = 0; y < height; y += 40)
        {
            graphics.DrawLine(pen, 0, y, width, y);
        }
    }

    private sealed class WorkflowNodeControl : Panel
    {
        private readonly IWorkflowNodeViewModel _node;
        private readonly Func<bool> _isWorkflowActive;
        private readonly Label _titleLabel;
        private readonly Label _stateLabel;
        private readonly Label _line1Label;
        private readonly Label _line2Label;
        private readonly Label _line3Label;
        private Point _lastMouse;
        private bool _dragging;

        public WorkflowNodeControl(IWorkflowNodeViewModel node, Func<bool> isWorkflowActive)
        {
            _node = node;
            _isWorkflowActive = isWorkflowActive;
            DoubleBuffered = true;
            Padding = new Padding(14);
            BackColor = Color.FromArgb(30, 41, 59);

            _titleLabel = CreateLabel(new Font("Microsoft YaHei UI", 11F, FontStyle.Bold), Color.White, 0, 0, Width - 28, 24);
            _stateLabel = CreateLabel(new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold), Color.Gainsboro, 0, 28, Width - 28, 20);
            _line1Label = CreateLabel(new Font("Microsoft YaHei UI", 8.5F), Color.FromArgb(226, 232, 240), 0, 52, Width - 28, 34);
            _line2Label = CreateLabel(new Font("Microsoft YaHei UI", 8.5F), Color.FromArgb(203, 213, 225), 0, 88, Width - 28, 34);
            _line3Label = CreateLabel(new Font("Microsoft YaHei UI", 8.5F), Color.FromArgb(226, 232, 240), 0, 124, Width - 28, 42);

            Controls.AddRange([_titleLabel, _stateLabel, _line1Label, _line2Label, _line3Label]);

            foreach (Control control in Controls)
            {
                WireDrag(control);
            }

            WireDrag(this);
            Resize += (_, _) => LayoutLabels();
            RefreshFromViewModel();
        }

        public void Disconnect()
        {
        }

        public void RefreshFromViewModel()
        {
            if (_node is ControllerViewModel controller)
            {
                _titleLabel.Text = "Network Flow Controller";
                _stateLabel.Text = controller.IsActive ? "State: Active" : "State: Idle";
                _line1Label.Text = $"Seed: {controller.SeedPayload}";
                _line2Label.Text = $"Seed: {controller.SeedPayload}";
                _line3Label.Text = "Controller 会触发整条请求链路。后续节点是否继续广播，仅由节点自身决定。";
            }
            else
            {
                var node = (NodeViewModel)_node;
                _titleLabel.Text = $"{node.Title}   {node.ExecutionOrderText}";
                _stateLabel.Text = $"Status: {node.LastStatus}";
                _line1Label.Text = $"Delay: {node.DelayMilliseconds} ms | Duration: {node.LastDuration}";
                _line2Label.Text = $"Run: {node.RunCount} | Queue: {node.WaitCount}";
                _line3Label.Text = string.IsNullOrWhiteSpace(node.LastError)
                    ? $"Preview: {node.LastResponsePreview}"
                    : $"Error: {node.LastError}";
            }

            RefreshVisualState();
        }

        public void RefreshVisualState()
        {
            if (_node is ControllerViewModel controller)
            {
                BackColor = controller.IsActive ? Color.FromArgb(15, 118, 110) : Color.FromArgb(30, 41, 59);
                _stateLabel.ForeColor = controller.IsActive ? Color.FromArgb(167, 243, 208) : Color.FromArgb(203, 213, 225);
                Invalidate();
                return;
            }

            var node = (NodeViewModel)_node;
            var (backColor, borderColor, foregroundColor) = ResolveNodePalette(node, _isWorkflowActive());
            BackColor = backColor;
            _stateLabel.ForeColor = foregroundColor;
            Tag = borderColor;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var borderColor = _node is ControllerViewModel controller
                ? (controller.IsActive ? Color.FromArgb(103, 232, 249) : Color.FromArgb(71, 85, 105))
                : Tag as Color? ?? Color.FromArgb(71, 85, 105);

            using var pen = new Pen(borderColor, 1.5f);
            using var path = CreateRoundRectanglePath(new Rectangle(0, 0, Width - 1, Height - 1), 18);
            e.Graphics.DrawPath(pen, path);
        }

        private void LayoutLabels()
        {
            var availableWidth = Width - 28;
            _titleLabel.SetBounds(14, 12, availableWidth, 24);
            _stateLabel.SetBounds(14, 38, availableWidth, 20);
            _line1Label.SetBounds(14, 64, availableWidth, 32);
            _line2Label.SetBounds(14, 96, availableWidth, 40);
            _line3Label.SetBounds(14, 136, availableWidth, Height - 150);
        }

        private void WireDrag(Control control)
        {
            control.MouseDown += OnMouseDown;
            control.MouseMove += OnMouseMove;
            control.MouseUp += OnMouseUp;
        }

        private void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (Parent is WorkflowSurfaceControl surface)
            {
                var localPoint = PointToClient(Control.MousePosition);
                if (TryGetSlotAt(localPoint, out var slot) && slot is not null)
                {
                    if (sender is Control control)
                    {
                        control.Capture = true;
                    }

                    surface.BeginConnection(slot);
                    surface.UpdateConnection(surface.PointToClient(Control.MousePosition));
                    _dragging = false;
                    return;
                }
            }

            _dragging = true;
            _lastMouse = Parent?.PointToClient(Control.MousePosition) ?? Point.Empty;
        }

        private void OnMouseMove(object? sender, MouseEventArgs e)
        {
            if (Parent is WorkflowSurfaceControl surface && surface.IsConnectionActive)
            {
                surface.UpdateConnection(surface.PointToClient(Control.MousePosition));
                return;
            }

            if (!_dragging || Parent is null)
            {
                return;
            }

            var current = Parent.PointToClient(Control.MousePosition);
            var delta = new Point(current.X - _lastMouse.X, current.Y - _lastMouse.Y);
            _lastMouse = current;

            if (delta.X != 0 || delta.Y != 0)
            {
                _node.MoveCommand.Execute(new Offset(delta.X, delta.Y));
            }
        }

        private void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (sender is Control control)
            {
                control.Capture = false;
            }

            if (Parent is WorkflowSurfaceControl surface && surface.IsConnectionActive)
            {
                surface.EndConnection(surface.PointToClient(Control.MousePosition));
                _dragging = false;
                return;
            }

            _dragging = false;
        }

        private bool TryGetSlotAt(Point localPoint, out IWorkflowSlotViewModel? slot)
        {
            slot = null;
            const double radius = 16d;

            if (_node is NodeViewModel node)
            {
                if (node.InputSlot is not null && IsWithinSlot(localPoint, 0d, Height / 2d, radius))
                {
                    slot = node.InputSlot;
                    return true;
                }

                if (node.OutputSlot is not null && IsWithinSlot(localPoint, Width, Height / 2d, radius))
                {
                    slot = node.OutputSlot;
                    return true;
                }

                return false;
            }

            if (_node is ControllerViewModel controller && controller.OutputSlot is not null && IsWithinSlot(localPoint, Width, Height / 2d, radius))
            {
                slot = controller.OutputSlot;
                return true;
            }

            return false;
        }

        private static bool IsWithinSlot(Point point, double centerX, double centerY, double radius)
        {
            var dx = point.X - centerX;
            var dy = point.Y - centerY;
            return (dx * dx) + (dy * dy) <= radius * radius;
        }

        private static Label CreateLabel(Font font, Color color, int left, int top, int width, int height)
            => new()
            {
                AutoSize = false,
                Left = left,
                Top = top,
                Width = width,
                Height = height,
                Font = font,
                ForeColor = color,
                BackColor = Color.Transparent
            };

        private static (Color BackColor, Color BorderColor, Color ForegroundColor) ResolveNodePalette(NodeViewModel node, bool isWorkflowActive)
        {
            if (node.IsRunning)
            {
                return (Color.FromArgb(29, 78, 216), Color.FromArgb(147, 197, 253), Color.FromArgb(219, 234, 254));
            }

            if (!string.IsNullOrWhiteSpace(node.LastError))
            {
                return (Color.FromArgb(127, 29, 29), Color.FromArgb(252, 165, 165), Color.FromArgb(254, 202, 202));
            }

            if (isWorkflowActive && (node.LastStatus.StartsWith('2') || node.LastStatus.StartsWith('3')))
            {
                return (Color.FromArgb(20, 83, 45), Color.FromArgb(134, 239, 172), Color.FromArgb(220, 252, 231));
            }

            return (Color.FromArgb(30, 41, 59), Color.FromArgb(71, 85, 105), Color.FromArgb(203, 213, 225));
        }

        private static System.Drawing.Drawing2D.GraphicsPath CreateRoundRectanglePath(Rectangle bounds, int radius)
        {
            var diameter = radius * 2;
            var path = new System.Drawing.Drawing2D.GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
