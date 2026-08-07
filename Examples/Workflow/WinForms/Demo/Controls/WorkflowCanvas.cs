using Demo.ViewModels;
using Demo.Workflow;
using System.Collections.ObjectModel;
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
public sealed class WorkflowCanvas : Panel, WorkflowBehaviors.IWorkflowGridDecorator
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

    // 连线叠层：透明 Panel，承载池化的模板 LinkView 子控件；随平移/滚动移动。
    private readonly TransparentLinksHost _linksHost;

    // 响应式连线集合（VirtualLink + 真实链接），由 _linksHost 上的 ViewManager 消费。
    private ObservableCollection<IWorkflowViewModel>? _linkItems;
    private IWorkflowTreeViewModel? _linksSubscribedTree;

    // 平移
    private bool _isPanning;
    private Point _panPressScreen;
    private Point _panOffsetAtPress;
    private Point _panOffset; // 世界坐标原点在客户端中的像素位置

    // ── IWorkflowGridDecorator ──────────────────────────────────────────────
    // WorkflowSurfaceBehavior.Refresh 在每次刷新周期把滚动/内容偏移推送到这里，
    // 供外部装饰器或诊断读取；画布自身的绘制仍使用内部 _panOffset 计算。
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetX { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ScrollOffsetY { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetX { get; set; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double ContentOffsetY { get; set; }

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
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetGridDecoratorName(this, nameof(WorkflowCanvas));
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetPointerPressSourceName(this, nameof(WorkflowCanvas));
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(this, true);

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        // 连线叠层：全尺寸透明 Panel，作为 ViewManager 的宿主承载模板 LinkView。
        // Enabled=false 使其不拦截鼠标（画布平移/节点交互不受影响）；透明背景让
        // 网格（画布 OnPaintBackground）透过叠层显示。
        _linksHost = new TransparentLinksHost
        {
            Location = Point.Empty,
            Size = new System.Drawing.Size(1920, 1080),
        };
        Controls.Add(_linksHost);

        // LinkView 工厂：新建模板 LinkView（透明，Dock=Fill，经 ViewModel 绑定 link）。
        var selector = new Views.TemplateSelector
        {
            LinkViewFactory = link =>
            {
                var view = new Views.LinkView
                {
                    Dock = DockStyle.Fill,
                    BackColor = Color.Transparent,
                };
                view.ViewModel = link;
                return view;
            },
        };
        WorkflowBehaviors.ViewPool.SetTemplateSelector(_linksHost, selector);
    }

    // ── Session 生命周期 ───────────────────────────────────────────────────────
    private void AttachSession(WorkflowDemoSession? s)
    {
        if (s is null) return;
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(this, s.Tree);
        s.Tree.Nodes.CollectionChanged += OnNodesChanged;
        s.Tree.Links.CollectionChanged += OnLinksChanged;
        s.Controller.PropertyChanged += OnControllerPropertyChanged;

        foreach (var node in s.Tree.Nodes) AddCard(node);
        UpdateCanvasMinSize();

        // 连线池：VirtualLink（首位）+ 全部真实 Link → _linksHost 上的 ViewManager。
        AttachLinksPool(s.Tree);

        // 延迟同步：等 WinForms 完成首次布局后再计算 SlotView 屏幕坐标
        if (IsHandleCreated)
            BeginInvoke(InitialSync);
        else
            HandleCreated += OnHandleCreatedForInitialSync;
    }

    /// <summary>
    /// 为连线叠层创建响应式链接集合（VirtualLink + 全部真实链接），交给 ViewManager
    /// 池化渲染为模板 LinkView。集合自身的变更会把 VirtualLink / 链接的增删自动
    /// 反映到池中，因此画布不再需要自己维护连线绘制。
    /// </summary>
    private void AttachLinksPool(IWorkflowTreeViewModel tree)
    {
        _linkItems = new ObservableCollection<IWorkflowViewModel> { tree.VirtualLink };
        foreach (var link in tree.Links)
        {
            _linkItems.Add(link);
        }

        _linksSubscribedTree = tree;
        tree.Links.CollectionChanged += OnLinksCollectionChanged;
        WorkflowBehaviors.ViewPool.SetItemsSource(_linksHost, _linkItems);
        PositionLinksHost();
    }

    private void OnLinksCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (InvokeRequired)
        {
            BeginInvoke(new NotifyCollectionChangedEventHandler(OnLinksCollectionChanged), sender, e);
            return;
        }

        if (_linkItems is null) return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add when e.NewItems is not null:
                foreach (var link in e.NewItems)
                {
                    _linkItems.Add((IWorkflowViewModel)link);
                }
                break;
            case NotifyCollectionChangedAction.Remove when e.OldItems is not null:
                foreach (var link in e.OldItems)
                {
                    _linkItems.Remove((IWorkflowViewModel)link);
                }
                break;
            case NotifyCollectionChangedAction.Reset:
                _linkItems.Clear();
                if (_linksSubscribedTree is not null)
                {
                    _linkItems.Add(_linksSubscribedTree.VirtualLink);
                    foreach (var link in _linksSubscribedTree.Links)
                    {
                        _linkItems.Add(link);
                    }
                }
                break;
        }

        SyncAllSlotAnchors();
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
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
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(this, null);
        HandleCreated -= OnHandleCreatedForInitialSync;
        s.Tree.Nodes.CollectionChanged -= OnNodesChanged;
        s.Tree.Links.CollectionChanged -= OnLinksChanged;
        s.Controller.PropertyChanged -= OnControllerPropertyChanged;

        if (_linksSubscribedTree is not null)
        {
            _linksSubscribedTree.Links.CollectionChanged -= OnLinksCollectionChanged;
        }

        _linksSubscribedTree = null;
        _linkItems = null;
        WorkflowBehaviors.ViewPool.SetItemsSource(_linksHost, null);

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

        PositionLinksHost();
        UpdateCanvasMinSize();
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

    /// <summary>
    /// 将连线叠层定位到世界坐标原点处（pan + scroll）。叠层内的 LinkView 用世界
    /// 坐标（slot.Anchor）画折线，因此叠层随画布平移/滚动整体移动即可保持对齐。
    /// 尺寸跟随当前内容范围。
    /// </summary>
    private void PositionLinksHost()
    {
        var scroll = AutoScrollPosition;
        var w = Math.Max(ClientSize.Width, 1920);
        var h = Math.Max(ClientSize.Height, 1080);

        // 注意：不要 BringToFront —— 连线叠层保持在卡片之下，连线下沉到节点后面
        // （与 WPF 模板的链接在节点背后的层级一致）。叠层本身透明且 Enabled=false，
        // 不会阻挡画布平移或节点/槽位交互。
        _linksHost.Location = new Point(
            _panOffset.X + scroll.X,
            _panOffset.Y + scroll.Y);
        _linksHost.Size = new System.Drawing.Size(w, h);
    }

    // ── Slot 锚点同步 ────────────────────────────────────────────────────────
    /// <summary>
    /// 计算所有 SlotView 的实时世界坐标，写入 IWorkflowSlotViewModel.Anchor，
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
        Views.SlotView? btn,
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

    private static IEnumerable<Views.SlotView> EnumerateSlotButtons(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Views.SlotView sb) yield return sb;
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
    // 网格与标尺画在 OnPaintBackground 中（而非 OnPaint），这样透明叠层的模板
    // LinkView 子控件（BackColor=Transparent）能透过自身合成显示网格 —— 这是
    // WinForms 透明子控件合成机制的硬性要求（子控件只合成父控件的
    // OnPaintBackground 输出）。
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var scroll = AutoScrollPosition;
        var origin = new PointF(_panOffset.X + scroll.X, _panOffset.Y + scroll.Y);

        DrawGrid(g, origin);
        DrawAxisScale(g, origin);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var scroll = AutoScrollPosition;
        var origin = new PointF(_panOffset.X + scroll.X, _panOffset.Y + scroll.Y);

        if (_session is null) return;

        // 实时计算所有 Slot 的世界坐标快照，写回 slot.Anchor（连线端点、命中测试用）
        var slotMap = BuildSlotWorldMap();
        foreach (var (slot, pt) in slotMap)
            slot.Anchor = new Anchor(pt.X, pt.Y, slot.Anchor.Layer);

        // 连线由模板 LinkView 子控件（透明叠层 _linksHost）绘制，画布本身不再画线。
        // 槽位由卡片内的模板 SlotView 绘制，画布不再画槽位圆圈。

        // 没有对应卡片的节点：绘制占位矩形
        foreach (var node in _session.Tree.Nodes)
        {
            if (!_cards.ContainsKey(node))
                DrawNodeFallback(g, node, origin);
        }
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

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        // 滚动时同步连线叠层位置（LinksHost 本身不会被 AutoScroll 移动）。
        PositionLinksHost();
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

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

/// <summary>
/// 透明的连线叠层面板：承载池化的模板 LinkView 子控件。透明背景让画布的网格
/// （画布 OnPaintBackground）透过显示；Enabled=false 使其不拦截鼠标（画布平移、
/// 节点拖拽、槽位连线都由画布与卡片上的 Behavior 处理）。
/// </summary>
internal sealed class TransparentLinksHost : Panel
{
    public TransparentLinksHost()
    {
        DoubleBuffered = true;
        BackColor = Color.Transparent;
        Enabled = false;
        TabStop = false;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.SupportsTransparentBackColor,
            true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 不填充不透明背景，保持透明以显示画布网格。
    }
}
