using Demo.ViewModels;
using Demo.Workflow;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Controls;

/// <summary>
/// 工作流画布控件。
///
/// 设计原则（纯 WinForms 实践）：
///   - 完全自绘网格、贝塞尔连线、槽位圆圈；节点卡片以子控件形式添加
///   - 画布平移：拖拽背景区域时更新 <see cref="_panOffset"/>，所有子控件随之偏移
///   - 节点拖拽：鼠标按下落在某节点卡片头部区域时，在 MouseMove 中持续调用
///     <see cref="IWorkflowNodeViewModel.MoveCommand"/> 并重新布局该卡片
///   - Slot 锚点：每次布局/绘制前通过控件屏幕坐标直接计算，无需独立 Behavior
///   - 画布大小：根据节点坐标动态计算，超出窗口区域后出现滚动条
/// </summary>
public sealed class WorkflowCanvas : Panel
{
    // ── 网格参数 ──────────────────────────────────────────────────────────────
    private const int GridSpacing = 40;
    private const int MajorFreq = 5;
    private const double Eps = 0.001;
    private const int TickLen = 8;
    private const int LabelPad = 4;

    // ── 状态 ──────────────────────────────────────────────────────────────────
    private WorkflowDemoSession? _session;
    private readonly Dictionary<IWorkflowNodeViewModel, WorkflowNodeCard> _cards = [];

    // 平移
    private bool _isPanning;
    private Point _panPressScreen;
    private Point _panOffsetAtPress;
    private Point _panOffset; // 世界坐标原点在客户端中的像素位置

    // ── 公共属性 ──────────────────────────────────────────────────────────────

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public WorkflowDemoSession? Session
    {
        get => _session;
        set
        {
            if (ReferenceEquals(_session, value)) return;
            DetachSession(_session);
            _session = value;
            AttachSession(value);
        }
    }

    // ── 构造 ──────────────────────────────────────────────────────────────────
    public WorkflowCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(15, 23, 42);
        AutoScroll = true;

        WorkflowBehaviors.WorkflowSurfaceBehavior.SetScrollViewerName(this, nameof(WorkflowCanvas));
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetCanvasName(this, nameof(WorkflowCanvas));
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetPointerPressSourceName(this, nameof(WorkflowCanvas));
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(this, true);

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
    }

    // ── Session 生命周期 ───────────────────────────────────────────────────────
    private void AttachSession(WorkflowDemoSession? s)
    {
        if (s is null) return;
        s.Tree.Nodes.CollectionChanged += OnNodesChanged;
        s.Tree.Links.CollectionChanged += OnLinksChanged;
        s.Controller.PropertyChanged += OnControllerPropertyChanged;

        foreach (var node in s.Tree.Nodes) AddCard(node);
        UpdateCanvasMinSize();

        // 延迟同步：等 WinForms 完成首次布局后再计算 SlotButton 屏幕坐标
        if (IsHandleCreated)
            BeginInvoke(InitialSync);
        else
            HandleCreated += OnHandleCreatedForInitialSync;
    }

    private void OnHandleCreatedForInitialSync(object? sender, EventArgs e)
    {
        HandleCreated -= OnHandleCreatedForInitialSync;
        BeginInvoke(InitialSync);
    }

    private void InitialSync()
    {
        SyncAllSlotAnchors();
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

    private void DetachSession(WorkflowDemoSession? s)
    {
        if (s is null) return;
        HandleCreated -= OnHandleCreatedForInitialSync;
        s.Tree.Nodes.CollectionChanged -= OnNodesChanged;
        s.Tree.Links.CollectionChanged -= OnLinksChanged;
        s.Controller.PropertyChanged -= OnControllerPropertyChanged;

        foreach (var card in _cards.Values)
        {
            Controls.Remove(card);
            card.Dispose();
        }

        _cards.Clear();
        WorkflowBehaviors.ViewPool.SetItemsSource(this, null);
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

    // ── 节点集合变更 ──────────────────────────────────────────────────────────
    private void OnNodesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => OnNodesChanged(sender, e))); return; }

        if (e.OldItems is not null)
        {
            foreach (var n in e.OldItems.OfType<IWorkflowNodeViewModel>())
                RemoveCard(n);
        }

        if (e.NewItems is not null)
        {
            foreach (var n in e.NewItems.OfType<IWorkflowNodeViewModel>())
                AddCard(n);
        }

        SyncAllSlotAnchors();
        UpdateCanvasMinSize();
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => OnLinksChanged(sender, e))); return; }
        // 新建/删除连线时重新同步所有槽位锚点，保证两端坐标正确
        SyncAllSlotAnchors();
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

    private void OnControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ControllerViewModel.IsActive)) return;
        if (InvokeRequired) { BeginInvoke(new Action(RefreshAllCards)); return; }
        RefreshAllCards();
    }

    // ── 节点卡片管理 ──────────────────────────────────────────────────────────
    private void AddCard(IWorkflowNodeViewModel node)
    {
        if (_cards.ContainsKey(node)) return;

        var card = new WorkflowNodeCard();
        card.Bind(node);

        // 订阅节点坐标/尺寸变化
        if (node is INotifyPropertyChanged n) n.PropertyChanged += OnNodePropertyChanged;

        _cards[node] = card;
        Controls.Add(card);
        LayoutCard(node, card);
        card.BringToFront();
    }

    private void RemoveCard(IWorkflowNodeViewModel node)
    {
        if (!_cards.TryGetValue(node, out var card)) return;
        if (node is INotifyPropertyChanged n) n.PropertyChanged -= OnNodePropertyChanged;
        card.Unbind();
        Controls.Remove(card);
        card.Dispose();
        _cards.Remove(node);
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(new PropertyChangedEventHandler(OnNodePropertyChanged), sender, e); return; }
        if (sender is not IWorkflowNodeViewModel node || !_cards.TryGetValue(node, out var card)) return;

        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor) or nameof(IWorkflowNodeViewModel.Size))
        {
            LayoutCard(node, card);
            SyncAllSlotAnchors();
            UpdateCanvasMinSize();
        }
        else
        {
            card.Refresh(node);
        }

        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

    private void RefreshAllCards()
    {
        foreach (var (node, card) in _cards)
        {
            card.RefreshVisual();
        }

        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

    /// <summary>将节点卡片定位到画布坐标对应的客户端位置。</summary>
    private void LayoutCard(IWorkflowNodeViewModel node, WorkflowNodeCard card)
    {
        card.Bounds = NodeBounds(node);
    }

    private Rectangle NodeBounds(IWorkflowNodeViewModel node)
    {
        var scroll = AutoScrollPosition;
        return new Rectangle(
            (int)Math.Round(node.Anchor.Horizontal + _panOffset.X + scroll.X),
            (int)Math.Round(node.Anchor.Vertical + _panOffset.Y + scroll.Y),
            (int)Math.Round(node.Size.Width),
            (int)Math.Round(node.Size.Height));
    }

    private void RelayoutAllCards()
    {
        foreach (var (node, card) in _cards)
        {
            LayoutCard(node, card);
        }

        UpdateCanvasMinSize();
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

    // ── Slot 锚点同步 ────────────────────────────────────────────────────────
    /// <summary>
    /// 计算所有 SlotButton 的实时世界坐标，写入 IWorkflowSlotViewModel.Anchor，
    /// 并返回一份 slot→世界坐标 的快照字典，供绘制使用。
    /// </summary>
    private Dictionary<IWorkflowSlotViewModel, PointF> BuildSlotWorldMap()
    {
        var scroll = AutoScrollPosition;
        var map = new Dictionary<IWorkflowSlotViewModel, PointF>(ReferenceEqualityComparer.Instance);
        foreach (var (_, card) in _cards)
            CollectNodeSlotPositions(card, map, scroll, _panOffset);
        return map;
    }

    private void SyncAllSlotAnchors()
    {
        var scroll = AutoScrollPosition;
        foreach (var (_, card) in _cards)
        {
            var map = new Dictionary<IWorkflowSlotViewModel, PointF>(ReferenceEqualityComparer.Instance);
            CollectNodeSlotPositions(card, map, scroll, _panOffset);
            foreach (var (slot, pt) in map)
                slot.Anchor = new Anchor(pt.X, pt.Y, slot.Anchor.Layer);
        }
    }

    private static void CollectNodeSlotPositions(
        WorkflowNodeCard card,
        Dictionary<IWorkflowSlotViewModel, PointF> map,
        Point scroll,
        Point panOffset)
    {
        // card.Left = node.Anchor.Horizontal + panOffset.X + scroll.X
        // 世界坐标（TranslateTransform(origin) 后使用）= node.Anchor.Horizontal + cx
        //           = card.Left - scroll.X - panOffset.X + cx
        var cardOriginX = card.Left - scroll.X - panOffset.X;
        var cardOriginY = card.Top - scroll.Y - panOffset.Y;

        CollectSlotButton(card.InputSlotButton, card, cardOriginX, cardOriginY, map);
        CollectSlotButton(card.OutputSlotButton, card, cardOriginX, cardOriginY, map);
        foreach (var btn in EnumerateSlotButtons(card))
            CollectSlotButton(btn, card, cardOriginX, cardOriginY, map);
    }

    private static void CollectSlotButton(
        SlotButton? btn,
        WorkflowNodeCard card,
        float cardOriginX,
        float cardOriginY,
        Dictionary<IWorkflowSlotViewModel, PointF> map)
    {
        if (btn is null || btn.ViewModel is null || !btn.Visible) return;
        if (map.ContainsKey(btn.ViewModel)) return;

        // 从 btn 向上遍历到 card，累加各级 Left/Top，得到 btn 中心在卡片内的相对坐标
        var cx = btn.Left + btn.Width / 2;
        var cy = btn.Top + btn.Height / 2;
        var cur = btn.Parent;
        while (cur is not null && !ReferenceEquals(cur, card))
        {
            cx += cur.Left;
            cy += cur.Top;
            cur = cur.Parent;
        }

        map[btn.ViewModel] = new PointF(cardOriginX + cx, cardOriginY + cy);
    }

    private static IEnumerable<SlotButton> EnumerateSlotButtons(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is SlotButton sb) yield return sb;
            foreach (var nested in EnumerateSlotButtons(child))
                yield return nested;
        }
    }

    // ── 鼠标事件（平移、节点拖拽、连线）────────────────────────────────────
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;

        if (_session?.Tree.VirtualLink.IsVisible == true)
        {
            if (_session.Tree.ResetVirtualLinkCommand.CanExecute(null))
            {
                _session.Tree.ResetVirtualLinkCommand.Execute(null);
            }

            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
            return;
        }

        // 卡片内子控件不会触发画布 MouseDown；只有点击空白区域才到这里 → 画布平移
        _isPanning = true;
        _panPressScreen = Cursor.Position;
        _panOffsetAtPress = _panOffset;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_isPanning)
        {
            var cur = Cursor.Position;
            _panOffset = new Point(
                _panOffsetAtPress.X + cur.X - _panPressScreen.X,
                _panOffsetAtPress.Y + cur.Y - _panPressScreen.Y);
            RelayoutAllCards();
            return;
        }

        // 连线模式下的鼠标追踪由 WorkflowSlotConnectionBehavior 处理，无需在此重复
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_isPanning)
        {
            _isPanning = false;
            Capture = false;
            return;
        }
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (!Capture)
        {
            _isPanning = false;
            // 连线状态由 WorkflowSlotConnectionBehavior 单独管理，这里不清除
        }
    }

    // ── 绘制 ──────────────────────────────────────────────────────────────────
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var scroll = AutoScrollPosition;
        var origin = new PointF(_panOffset.X + scroll.X, _panOffset.Y + scroll.Y);

        DrawGrid(g, origin);
        DrawAxisScale(g, origin);

        if (_session is null) return;

        // 实时计算所有 Slot 的世界坐标快照，不依赖缓存
        var slotMap = BuildSlotWorldMap();

        // 同步写回 slot.Anchor，保证 HitTest 等数据层面也能使用最新坐标
        foreach (var (slot, pt) in slotMap)
            slot.Anchor = new Anchor(pt.X, pt.Y, slot.Anchor.Layer);

        // 连线和槽位在世界坐标系中绘制，需要偏移 origin
        var state = g.Save();
        g.TranslateTransform(origin.X, origin.Y);

        // 已确认连线
        using var linkPen = new Pen(Color.FromArgb(34, 211, 238), 4f);
        using var linkDot = new SolidBrush(Color.FromArgb(103, 232, 249));
        foreach (var link in _session.Tree.Links.Where(l => l.IsVisible))
            DrawLink(g, linkPen, linkDot, link, slotMap);

        // 虚线（连线中）
        if (_session.Tree.VirtualLink.IsVisible)
        {
            using var vPen = new Pen(Color.FromArgb(226, 232, 240), 3f);
            using var vDot = new SolidBrush(Color.FromArgb(226, 232, 240));
            DrawLink(g, vPen, vDot, _session.Tree.VirtualLink, slotMap);
        }

        // 槽位圆圈（覆盖在卡片上方）
        using var slotPen = new Pen(Color.White, 1.5f);
        foreach (var node in _session.Tree.Nodes)
        {
            foreach (var slot in EnumerateNodeSlots(node))
                DrawSlot(g, slotPen, slot, slotMap);
        }

        g.Restore(state);

        // 没有对应卡片的节点：绘制占位矩形
        foreach (var node in _session.Tree.Nodes)
        {
            if (!_cards.ContainsKey(node))
                DrawNodeFallback(g, node, origin);
        }
    }

    private static void DrawLink(
        Graphics g, Pen pen, Brush dotBrush,
        IWorkflowLinkViewModel link,
        Dictionary<IWorkflowSlotViewModel, PointF> slotMap)
    {
        // 优先从实时快照取坐标，回退到缓存的 slot.Anchor
        slotMap.TryGetValue(link.Sender, out var sp);
        slotMap.TryGetValue(link.Receiver, out var rp);
        var sx = sp != default ? sp.X : (float)link.Sender.Anchor.Horizontal;
        var sy = sp != default ? sp.Y : (float)link.Sender.Anchor.Vertical;
        var ex = rp != default ? rp.X : (float)link.Receiver.Anchor.Horizontal;
        var ey = rp != default ? rp.Y : (float)link.Receiver.Anchor.Vertical;
        var cp = Math.Max(80f, MathF.Abs(ex - sx) * 0.45f);

        using var path = new GraphicsPath();
        path.AddBezier(sx, sy, sx + cp, sy, ex - cp, ey, ex, ey);
        g.DrawPath(pen, path);
        g.FillEllipse(dotBrush, sx - 5, sy - 5, 10, 10);
        g.FillEllipse(dotBrush, ex - 5, ey - 5, 10, 10);
    }

    private static void DrawSlot(
        Graphics g, Pen pen,
        IWorkflowSlotViewModel slot,
        Dictionary<IWorkflowSlotViewModel, PointF> slotMap)
    {
        slotMap.TryGetValue(slot, out var pt);
        var x = (pt != default ? pt.X : (float)slot.Anchor.Horizontal) - 10;
        var y = (pt != default ? pt.Y : (float)slot.Anchor.Vertical) - 10;
        using var brush = new SolidBrush(SlotColor(slot.State));
        g.FillEllipse(brush, x, y, 20, 20);
        g.DrawEllipse(pen, x, y, 20, 20);
    }

    private static void DrawNodeFallback(Graphics g, IWorkflowNodeViewModel node, PointF origin)
    {
        if (node.Size.Width <= 0 || node.Size.Height <= 0) return;
        var bounds = new RectangleF(
            (float)(node.Anchor.Horizontal + origin.X),
            (float)(node.Anchor.Vertical + origin.Y),
            (float)node.Size.Width,
            (float)node.Size.Height);

        using var body = new SolidBrush(Color.FromArgb(37, 37, 37));
        using var border = new Pen(Color.FromArgb(75, 85, 99), 1.5f);
        using var path = RoundRectF(bounds, 18f);
        g.FillPath(body, path);
        g.DrawPath(border, path);
    }

    // ── 网格绘制 ──────────────────────────────────────────────────────────────
    private void DrawGrid(Graphics g, PointF origin)
    {
        using var minor = new Pen(Color.FromArgb(45, 66, 94), 1f);
        using var major = new Pen(Color.FromArgb(72, 103, 145), 1f);
        using var axis = new Pen(Color.FromArgb(56, 189, 248), 1.2f);

        var left = -origin.X;
        var top = -origin.Y;
        var right = left + ClientSize.Width;
        var bottom = top + ClientSize.Height;

        var startX = Math.Floor(left / GridSpacing) * GridSpacing;
        var startY = Math.Floor(top / GridSpacing) * GridSpacing;

        for (var x = startX; x <= right + GridSpacing; x += GridSpacing)
        {
            var sx = (float)(x + origin.X);
            var pen = NearZero(x) ? axis : IsMajor(x) ? major : minor;
            g.DrawLine(pen, sx, 0, sx, ClientSize.Height);
        }

        for (var y = startY; y <= bottom + GridSpacing; y += GridSpacing)
        {
            var sy = (float)(y + origin.Y);
            var pen = NearZero(y) ? axis : IsMajor(y) ? major : minor;
            g.DrawLine(pen, 0, sy, ClientSize.Width, sy);
        }
    }

    private void DrawAxisScale(Graphics g, PointF origin)
    {
        var left = -origin.X;
        var top = -origin.Y;
        var right = left + ClientSize.Width;
        var bottom = top + ClientSize.Height;
        var axisX = origin.X;
        var axisY = origin.Y;

        using var tickPen = new Pen(Color.FromArgb(100, 116, 139), 1f);
        using var textBrush = new SolidBrush(Color.FromArgb(148, 163, 184));
        using var font = new Font("Segoe UI", 8.5f, FontStyle.Regular);
        using var fmt = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, FormatFlags = StringFormatFlags.NoWrap };

        var startX = Math.Floor(left / GridSpacing) * GridSpacing;
        var startY = Math.Floor(top / GridSpacing) * GridSpacing;

        if (axisY >= 0 && axisY <= ClientSize.Height)
        {
            for (var x = startX; x <= right + GridSpacing; x += GridSpacing)
            {
                var sx = (float)(x + origin.X);
                var tl = IsMajor(x) ? TickLen : TickLen / 2f;
                g.DrawLine(tickPen, sx, axisY - tl, sx, axisY + tl);
                if (!NearZero(x) && IsMajor(x))
                    g.DrawString(((int)Math.Round(x)).ToString(), font, textBrush,
                        new RectangleF(sx + LabelPad, axisY + LabelPad, 64, 18), fmt);
            }
        }

        if (axisX >= 0 && axisX <= ClientSize.Width)
        {
            for (var y = startY; y <= bottom + GridSpacing; y += GridSpacing)
            {
                var sy = (float)(y + origin.Y);
                var tl = IsMajor(y) ? TickLen : TickLen / 2f;
                g.DrawLine(tickPen, axisX - tl, sy, axisX + tl, sy);
                if (!NearZero(y) && IsMajor(y))
                    g.DrawString(((int)Math.Round(y)).ToString(), font, textBrush,
                        new RectangleF(axisX + LabelPad, sy + LabelPad, 64, 18), fmt);
            }
        }
    }

    // ── 命中测试 ──────────────────────────────────────────────────────────────
    private IWorkflowSlotViewModel? HitTestSlot(Anchor worldAnchor, IWorkflowSlotViewModel? exclude = null)
    {
        if (_session is null) return null;
        const double r2 = 18.0 * 18.0;
        foreach (var slot in EnumerateAllSlots())
        {
            if (ReferenceEquals(slot, exclude)) continue;
            var dx = slot.Anchor.Horizontal - worldAnchor.Horizontal;
            var dy = slot.Anchor.Vertical - worldAnchor.Vertical;
            if (dx * dx + dy * dy <= r2) return slot;
        }

        return null;
    }

    private IEnumerable<IWorkflowSlotViewModel> EnumerateAllSlots()
    {
        if (_session is null) yield break;
        foreach (var node in _session.Tree.Nodes)
        foreach (var slot in EnumerateNodeSlots(node))
            yield return slot;
    }

    private static IEnumerable<IWorkflowSlotViewModel> EnumerateNodeSlots(IWorkflowNodeViewModel node)
    {
        switch (node)
        {
            case BoolSelectorNodeViewModel b:
                if (b.InputSlot is not null) yield return b.InputSlot;
                if (b.TrueSlot is not null) yield return b.TrueSlot;
                if (b.FalseSlot is not null) yield return b.FalseSlot;
                break;
            case EnumSelectorNodeViewModel e:
                if (e.InputSlot is not null) yield return e.InputSlot;
                if (e.OutputSlots is not null)
                {
                    foreach (var s in e.OutputSlots.Cast<IWorkflowSlotViewModel>())
                        yield return s;
                }
                break;
            case NodeViewModel nv:
                if (nv.InputSlot is not null) yield return nv.InputSlot;
                if (nv.OutputSlot is not null) yield return nv.OutputSlot;
                break;
            case ControllerViewModel cv:
                if (cv.OutputSlot is not null) yield return cv.OutputSlot;
                break;
        }
    }

    // ── 坐标转换 ──────────────────────────────────────────────────────────────
    private Anchor ClientToWorld(Point clientPt)
    {
        var scroll = AutoScrollPosition;
        return new Anchor(
            clientPt.X - _panOffset.X - scroll.X,
            clientPt.Y - _panOffset.Y - scroll.Y,
            0);
    }

    // ── 画布大小 ──────────────────────────────────────────────────────────────
    private void UpdateCanvasMinSize()
    {
        if (_session is null || _session.Tree.Nodes.Count == 0)
        {
            AutoScrollMinSize = new System.Drawing.Size(1280, 760);
            return;
        }

        var maxX = _session.Tree.Nodes.Max(n => n.Anchor.Horizontal + n.Size.Width);
        var maxY = _session.Tree.Nodes.Max(n => n.Anchor.Vertical + n.Size.Height);
        var w = (int)Math.Ceiling(maxX + _panOffset.X + 120);
        var h = (int)Math.Ceiling(maxY + _panOffset.Y + 120);
        AutoScrollMinSize = new System.Drawing.Size(Math.Max(1280, w), Math.Max(760, h));
    }

    // 阻止 WinForms 将子控件滚动到视图内（会干扰平移逻辑）
    protected override Point ScrollToControl(Control activeControl) => DisplayRectangle.Location;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_session?.Tree.ResetVirtualLinkCommand.CanExecute(null) == true)
            {
                _session.Tree.ResetVirtualLinkCommand.Execute(null);
            }

            DetachSession(_session);
        }

        base.Dispose(disposing);
    }

    // ── 静态辅助 ──────────────────────────────────────────────────────────────
    private static bool IsMajor(double v)
    {
        var major = GridSpacing * MajorFreq;
        var norm = ((v % major) + major) % major;
        return norm < Eps || Math.Abs(norm - major) < Eps;
    }

    private static bool NearZero(double v) => Math.Abs(v) < Eps;

    private static Color SlotColor(SlotState state) => state switch
    {
        var s when s.HasFlag(SlotState.Sender) && s.HasFlag(SlotState.Receiver) => Color.Violet,
        var s when s.HasFlag(SlotState.Sender) => Color.Tomato,
        var s when s.HasFlag(SlotState.Receiver) => Color.Lime,
        _ => Color.White,
    };

    private static GraphicsPath RoundRectF(RectangleF r, float radius)
    {
        var d = radius * 2f;
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
