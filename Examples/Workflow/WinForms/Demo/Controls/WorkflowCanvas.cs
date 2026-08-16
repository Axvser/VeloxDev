using Demo.ViewModels;
using Demo.Workflow;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
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
    // 标尺带厚度。其余方案代码同为 28px，但用户反馈 WinForms 视觉上偏小，故放大到 36px。
    private const int RulerThickness = 36;

    // ── 状态 ──────────────────────────────────────────────────────────────────
    private WorkflowDemoSession? _session;
    private readonly Dictionary<IWorkflowNodeViewModel, WorkflowNodeCard> _cards = [];

    // 连线渲染器：模板 LinkView（VirtualLink + 全部真实链接）。它们不是子控件，而是
    // 复用模板 LinkView 的几何（Render），由画布 OnPaint 统一绘制 —— 避免 WinForms 中
    // 重叠全尺寸透明兄弟窗口被 WS_CLIPSIBLINGS 裁掉（仅最上层可绘制）的问题。
    private readonly List<Views.LinkView> _linkRenderers = [];

    // 平移
    private bool _isPanning;
    private Point _panPressScreen;
    private Point _panOffsetAtPress;
    // 世界坐标原点在客户端中的像素位置。默认落在内容区左上角 (RulerThickness,
    // RulerThickness)，与其余方案一致：内容从标尺带右侧/下方开始，刻度“0”出现在
    // 内容边界而非左上角交界区。
    private Point _panOffset = new(RulerThickness, RulerThickness);

    // 小地图：宿主在 splitContainer.Panel2（非滚动区），由 SyncMinimap 手动同步。
    // 不使用 SetMinimapOverlayName —— Refresh 的 ResolveScrollOffset 只返回
    // -AutoScrollPosition，不含 _panOffset，手动同步才是确定性的。
    private Control? _minimap;

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

    /// <summary>
    /// 可选的小地图覆盖层。宿主应把它放在非滚动区（如 splitContainer.Panel2）并
    /// <c>BringToFront</c>，使画布平移/滚动时小地图固定不动。画布负责同步可见区域
    /// 并响应小地图的视口拖动请求。
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? MinimapOverlay
    {
        get => _minimap;
        set
        {
            if (ReferenceEquals(_minimap, value)) return;
            if (_minimap is not null)
            {
                if (_minimap is Views.IWorkflowMinimapScrollSource oldSrc)
                {
                    oldSrc.ViewportScrollRequested -= OnMinimapScrollRequested;
                }

                if (_minimap is WorkflowBehaviors.IWorkflowMinimapOverlay oldMm)
                {
                    oldMm.WorkflowTree = null;
                }
            }

            _minimap = value;
            if (value is not null)
            {
                if (value is Views.IWorkflowMinimapScrollSource src)
                {
                    src.ViewportScrollRequested += OnMinimapScrollRequested;
                }

                if (value is WorkflowBehaviors.IWorkflowMinimapOverlay mm)
                {
                    mm.WorkflowTree = _session?.Tree;
                }

                SyncMinimap();
            }
        }
    }

    // ── 构造 ──────────────────────────────────────────────────────────────────
    public WorkflowCanvas()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(30, 30, 30); // #1E1E1E 模板网格装饰器默认背景
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

        // 连线渲染器：VirtualLink（首位）+ 全部真实 Link → 画布 OnPaint 统一绘制。
        AttachLinksPool();
        SyncMinimap();

        // 延迟同步：等 WinForms 完成首次布局后再计算 SlotView 屏幕坐标
        if (IsHandleCreated)
            BeginInvoke(InitialSync);
        else
            HandleCreated += OnHandleCreatedForInitialSync;
    }

    /// <summary>
    /// 建立画布连线渲染器（VirtualLink + 全部真实链接）。渲染器复用模板 LinkView，
    /// 但不加入控件树，由画布 OnPaint 统一调用其 <see cref="Views.LinkView.Render"/>；
    /// 链接增删由 <see cref="OnLinksChanged"/> 触发重建。
    /// </summary>
    private void AttachLinksPool()
    {
        RebuildLinkRenderers();
    }

    private void RebuildLinkRenderers()
    {
        foreach (var lv in _linkRenderers) lv.Dispose();
        _linkRenderers.Clear();
        if (_session is null) return;

        _linkRenderers.Add(CreateLinkRenderer(_session.Tree.VirtualLink));
        foreach (var link in _session.Tree.Links)
        {
            _linkRenderers.Add(CreateLinkRenderer(link));
        }
    }

    private static Views.LinkView CreateLinkRenderer(IWorkflowLinkViewModel link)
    {
        var view = new Views.LinkView();
        view.ViewModel = link;
        return view;
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
        SyncMinimap();
    }

    // ── 小地图同步 ─────────────────────────────────────────────────────────────
    /// <summary>
    /// 把当前可见世界区域推送到小地图。节点卡片按 anchor + panOffset + scroll
    /// 定位（<see cref="NodeBounds"/>），node.Anchor 已是最终屏幕世界坐标 —— 该
    /// 自绘画布不再套用内容偏移平移，故小地图 ContentOffset 恒为 0，
    /// ScrollOffset = -(panOffset + scroll) 恰好等于屏幕左缘对应的世界坐标。
    /// </summary>
    private void SyncMinimap()
    {
        if (_minimap is not WorkflowBehaviors.IWorkflowMinimapOverlay m) return;

        var scroll = AutoScrollPosition;
        m.ScrollOffsetX = -(_panOffset.X + scroll.X);
        m.ScrollOffsetY = -(_panOffset.Y + scroll.Y);
        m.ContentOffsetX = 0;
        m.ContentOffsetY = 0;
        m.ViewportWidth = ClientSize.Width;
        m.ViewportHeight = ClientSize.Height;
        m.WorkflowTree = _session?.Tree;
        _minimap.Invalidate();
    }

    /// <summary>
    /// 小地图拖动请求滚动：小地图表达的是绝对的世界可见区域，故先把 AutoScroll
    /// 归零（鼠标滚轮滚过之后 AutoScrollPosition 非 0，会与平移打架，且会在
    /// RelayoutAllCards → UpdateCanvasMinSize 里被重新钳位，导致视口块反复回弹、
    /// 看起来拖不动），再按 ScrollOffset = -panOffset 反解 panOffset = -sx，
    /// 最后重排卡片（顺带再次同步小地图）。
    /// </summary>
    private void OnMinimapScrollRequested(double sx, double sy)
    {
        AutoScrollPosition = Point.Empty;
        _panOffset = new Point((int)Math.Round(-sx), (int)Math.Round(-sy));
        RelayoutAllCards();

        // AutoScrollPosition = Point.Empty 上方的 Scroll 事件会在 _panOffset 更新前触发
        // SyncMinimap，而它的生效是异步的 —— RelayoutAllCards 里的 SyncMinimap 仍可能读到
        // 旧的 AutoScrollPosition，把小地图 ScrollOffset 写回旧位置（块看起来"按下不动、
        // 拖一下才动"）。这里直接把小地图同步到请求的滚动目标，块立刻跟随。
        if (_minimap is WorkflowBehaviors.IWorkflowMinimapOverlay m)
        {
            m.ScrollOffsetX = sx;
            m.ScrollOffsetY = sy;
            m.ViewportWidth = ClientSize.Width;
            m.ViewportHeight = ClientSize.Height;
            _minimap.Invalidate();
        }

        // 小地图拖动期间鼠标捕获在小地图上，画布未持有 Capture —— Refresh 里
        // host.Capture 的同步重绘分支不会触发，只有异步 Invalidate。高频拖动时
        // WM_PAINT 被 WM_MOUSEMOVE 不断延后，节点旧位置与旧连线来不及擦除形成残影；
        // 这里同步重绘画布（网格/标尺/连线），与 Trimmed 版 ApplyPan 的 Update 一致。
        Update();
    }

    private void DetachSession(WorkflowDemoSession? s)
    {
        if (s is null) return;
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(this, null);
        HandleCreated -= OnHandleCreatedForInitialSync;
        s.Tree.Nodes.CollectionChanged -= OnNodesChanged;
        s.Tree.Links.CollectionChanged -= OnLinksChanged;
        s.Controller.PropertyChanged -= OnControllerPropertyChanged;

        foreach (var lv in _linkRenderers) lv.Dispose();
        _linkRenderers.Clear();

        foreach (var card in _cards.Values)
        {
            Controls.Remove(card);
            card.Dispose();
        }

        _cards.Clear();
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
        SyncMinimap();
    }

    private void OnLinksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (InvokeRequired) { BeginInvoke(new Action(() => OnLinksChanged(sender, e))); return; }
        // 新建/删除连线时重建渲染器并重新同步所有槽位锚点，保证两端坐标正确
        RebuildLinkRenderers();
        SyncAllSlotAnchors();
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        SyncMinimap();
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
        SyncMinimap();
    }

    private void RefreshAllCards()
    {
        foreach (var (node, card) in _cards)
        {
            card.RefreshVisual();
        }

        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        SyncMinimap();
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
        SyncMinimap();
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
            {
                slot.Anchor = new Anchor(pt.X, pt.Y, slot.Anchor.Layer);
            }
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
    // 网格与标尺画在 OnPaintBackground 中（而非 OnPaint），作为最底层垫底；卡片
    // 为子控件绘制在其上，连线由 OnPaint 的模板 LinkView 几何绘制在其上。
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var scroll = AutoScrollPosition;
        var origin = new PointF(_panOffset.X + scroll.X, _panOffset.Y + scroll.Y);

        DrawGrid(g, origin);
        DrawFloatingRulers(g, origin);
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

        // 连线：模板 LinkView 的几何在画布上统一绘制（TranslateTransform(origin) 后
        // 使用世界坐标）。渲染器不加入控件树，规避 WinForms 重叠全尺寸兄弟窗口被
        // WS_CLIPSIBLINGS 裁掉的问题；槽位由卡片内的模板 SlotView 绘制。
        var linkState = g.Save();
        g.TranslateTransform(origin.X, origin.Y);
        foreach (var lv in _linkRenderers)
        {
            lv.Render(g);
        }
        g.Restore(linkState);

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
    /// <summary>
    /// 绘制内容区网格（从 (RulerThickness, RulerThickness) 开始，随内容滚动平移）。
    /// 与其余方案（WPF/Avalonia/MAUI/WinUI 模板、Blazor）一致：网格属于内容，标尺悬浮。
    /// </summary>
    private void DrawGrid(Graphics g, PointF origin)
    {
        // 配色对齐 WinForms 网格装饰器模板默认（#2A2D2E / #3A3D40 / 轴线 #4D4D4D）。
        using var minor = new Pen(Color.FromArgb(42, 45, 46), 1f);
        using var major = new Pen(Color.FromArgb(58, 61, 64), 1f);
        using var axis = new Pen(Color.FromArgb(77, 77, 77), 1.2f);

        var contentLeft = RulerThickness;
        var contentTop = RulerThickness;
        var contentRight = ClientSize.Width;
        var contentBottom = ClientSize.Height;

        // 网格仅绘制在内容区（标尺带之外），与其余方案的 PushClip(contentRect) 一致。
        var gridState = g.Save();
        g.SetClip(new RectangleF(contentLeft, contentTop, contentRight - contentLeft, contentBottom - contentTop));

        // 世界坐标换算到客户端：x 落在内容区内（>= RulerThickness）的才画。
        var startX = Math.Floor((-origin.X) / GridSpacing) * GridSpacing;
        for (var x = startX; x <= -origin.X + contentRight + GridSpacing; x += GridSpacing)
        {
            var sx = (float)(x + origin.X);
            if (sx < contentLeft - GridSpacing) continue;
            var pen = NearZero(x) ? axis : IsMajor(x) ? major : minor;
            g.DrawLine(pen, sx, contentTop, sx, contentBottom);
        }

        var startY = Math.Floor((-origin.Y) / GridSpacing) * GridSpacing;
        for (var y = startY; y <= -origin.Y + contentBottom + GridSpacing; y += GridSpacing)
        {
            var sy = (float)(y + origin.Y);
            if (sy < contentTop - GridSpacing) continue;
            var pen = NearZero(y) ? axis : IsMajor(y) ? major : minor;
            g.DrawLine(pen, contentLeft, sy, contentRight, sy);
        }

        g.Restore(gridState);
    }

    /// <summary>
    /// 绘制悬浮标尺：固定在客户端左上角 36px 刻度带，刻度按网格间距对齐、随
    /// 滚动平移（translateX/Y = -origin），与其余方案的悬浮标尺行为一致。不再随
    /// 内容一起滚动（区别于旧的画在世界原点的 DrawAxisScale）。
    /// </summary>
    private void DrawFloatingRulers(Graphics g, PointF origin)
    {
        // 与其余方案（WPF/Avalonia/MAUI/WinUI Demo）悬浮标尺完全一致：
        //   - 标尺带 36px，世界原点默认落在内容边界，刻度“0”出现在内容交界处
        //   - 顶部/左侧刻度分别裁剪到各自带内，左上角交界区不渲染刻度线或数字
        //   - 顶部标签垂直居中 (ruler-13)/2，左侧标签 y+2；字号 13px
        // 配色保持模板默认：标尺底 #252526 / 刻度 #555555 / 文字 #888888 / 分隔 #3A3D40 / 轴线 #4D4D4D。
        using var rulerBrush = new SolidBrush(Color.FromArgb(37, 37, 38));
        using var dividerPen = new Pen(Color.FromArgb(58, 61, 64), 1f);
        using var tickPen = new Pen(Color.FromArgb(85, 85, 85), 1f);
        using var axisPen = new Pen(Color.FromArgb(77, 77, 77), 1f);
        using var textBrush = new SolidBrush(Color.FromArgb(136, 136, 136));
        using var font = new Font("Segoe UI", 13f, GraphicsUnit.Pixel);
        using var fmt = new StringFormat(StringFormat.GenericTypographic);

        var ruler = RulerThickness;
        var cw = ClientSize.Width;
        var ch = ClientSize.Height;

        // 刻度带背景。
        g.FillRectangle(rulerBrush, 0, 0, cw, ruler);
        g.FillRectangle(rulerBrush, 0, 0, ruler, ch);
        // 分隔线（把标尺与内容区隔开）。
        g.DrawLine(dividerPen, ruler, 0, ruler, ch);
        g.DrawLine(dividerPen, 0, ruler, cw, ruler);

        // 顶部标尺：世界 x 轴。刻度 x = value + origin.X；仅内容区上方（x >= ruler）绘制，
        // 左上角交界区不渲染刻度/数字。
        var worldLeft = -origin.X;
        var worldRight = worldLeft + cw;
        var startX = Math.Floor(worldLeft / GridSpacing) * GridSpacing;
        var topState = g.Save();
        g.SetClip(new RectangleF(ruler, 0, cw - ruler, ruler));
        for (var x = startX; x <= worldRight + GridSpacing; x += GridSpacing)
        {
            var sx = (float)(x + origin.X);
            var isMajor = IsMajor(x);
            var tl = isMajor ? ruler - 6f : Math.Max(6f, (float)(ruler * 0.35));
            g.DrawLine(NearZero(x) ? axisPen : tickPen, sx, ruler, sx, ruler - tl);
            if (isMajor)
            {
                g.DrawString(FormatGridValue(x), font, textBrush, sx + 3, (ruler - 13) / 2, fmt);
            }
        }

        g.Restore(topState);

        // 左侧标尺：世界 y 轴。仅内容区左侧（y >= ruler）绘制，左上角交界区不渲染刻度/数字。
        var worldTop = -origin.Y;
        var worldBottom = worldTop + ch;
        var startY = Math.Floor(worldTop / GridSpacing) * GridSpacing;
        var leftState = g.Save();
        g.SetClip(new RectangleF(0, ruler, ruler, ch - ruler));
        for (var y = startY; y <= worldBottom + GridSpacing; y += GridSpacing)
        {
            var sy = (float)(y + origin.Y);
            var isMajor = IsMajor(y);
            var tl = isMajor ? ruler - 6f : Math.Max(6f, (float)(ruler * 0.35));
            g.DrawLine(NearZero(y) ? axisPen : tickPen, ruler, sy, ruler - tl, sy);
            if (isMajor)
            {
                g.DrawString(FormatGridValue(y), font, textBrush, 3, sy + 2, fmt);
            }
        }

        g.Restore(leftState);
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
        // 连线渲染器不参与控件树，平移/滚动由画布 OnPaint 的 origin 变换统一处理。
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        SyncMinimap();
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

    /// <summary>刻度数值格式，与模板 FormatGridValue 一致（万级 K、百万级 M）。</summary>
    private static string FormatGridValue(double value)
    {
        var abs = Math.Abs(value);
        if (abs < 10000)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        if (abs < 1000000)
        {
            return Math.Round(value / 1000d, 1).ToString(CultureInfo.InvariantCulture) + "K";
        }

        return Math.Round(value / 1000000d, 1).ToString(CultureInfo.InvariantCulture) + "M";
    }

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
