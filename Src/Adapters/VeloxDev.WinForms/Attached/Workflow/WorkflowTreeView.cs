using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem.StandardEx;
using Size = System.Drawing.Size;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// The workflow surface for WinForms: chrome, grid, rulers, the scrolled canvas, the pooled node/link views and
/// the optional minimap, assembled and wired to <see cref="WorkflowSurfaceBehavior"/>.
/// </summary>
/// <remarks>
/// <para>
/// Derive from it and supply the two view factories; everything else — the signed-pan engine, the self-drawn
/// grid, the per-pixel-alpha ruler overlay (an owned layered popup; WinForms has no transparent child
/// compositing), the view pool wiring and the layout scheduling — is platform machinery that would be identical
/// in every host.
/// </para>
/// <para>
/// The surface keeps the canvas fixed over the viewport and translates each node card by the pan instead of
/// moving the canvas. A content-sized canvas that moves would clip cards whose canvas-local position goes
/// negative, which made the left/top regions look like they had no canvas at all.
/// </para>
/// </remarks>
public abstract class WorkflowTreeView : UserControl
{
    /// <summary>
    /// Visual-only content inset, equal to the ruler band, so content sits below/right of the floating rulers and
    /// the world axes land on their inner corner.
    /// </summary>
    /// <remarks>
    /// A screen-space translate only: the numbers reported through <see cref="IWorkflowTreeViewModelHelper.Viewport"/>
    /// still exclude it, so they match the other adapters.
    /// </remarks>
    public const double RulerReserve = SurfaceCanvas.DefaultRulerThickness;

    /// <summary>Viewport host the surface behaviour reads scroll offsets from.</summary>
    public ScrollableControl PART_ScrollViewer { get; }

    /// <summary>
    /// Canvas hosting the pooled node and link views. It paints the grid in its own background pass; the views
    /// are children and repaint after it.
    /// </summary>
    public Panel PART_Canvas { get; }

    /// <summary>
    /// The grid/ruler surface. The canvas implements <see cref="IWorkflowGridDecorator"/> itself, so this is the
    /// same control under its decorator name — which is what the surface behaviour resolves.
    /// </summary>
    public Control PART_GridDecorator => PART_Canvas;

    /// <summary>The minimap overlay, when one has been assigned.</summary>
    public Control? PART_MinimapOverlay { get; private set; }

    /// <summary>Gets or sets the workflow tree bound to this surface.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowTreeViewModel? ViewModel
    {
        get => _tree;
        set
        {
            if (ReferenceEquals(_tree, value)) return;

            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnTreeChanged;
                _notifier = null;
            }

            _tree = value;
            Tag = value;

            if (value is INotifyPropertyChanged n)
            {
                _notifier = n;
                n.PropertyChanged += OnTreeChanged;
            }

            AttachTree();
            OnTreeAttached(value);
            ScheduleLayout();
        }
    }

    /// <summary>Gets or sets the minimap overlay (set to null to hide it).</summary>
    /// <remarks>
    /// A minimap that implements <see cref="IWorkflowMinimapScrollSource"/> has its viewport drags wired to the
    /// surface's pan here.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Control? MinimapOverlay
    {
        get => PART_MinimapOverlay;
        set
        {
            if (ReferenceEquals(PART_MinimapOverlay, value)) return;
            if (PART_MinimapOverlay is not null)
            {
                if (PART_MinimapOverlay is IWorkflowMinimapScrollSource old)
                {
                    old.ViewportScrollRequested -= OnMinimapScrollRequested;
                }

                Controls.Remove(PART_MinimapOverlay);
                PART_MinimapOverlay = null;
            }

            if (value is null) return;

            PART_MinimapOverlay = value;
            if (value is IWorkflowMinimapScrollSource minimap)
            {
                minimap.ViewportScrollRequested += OnMinimapScrollRequested;
            }

            value.Visible = true;
            value.Name = "PART_MinimapOverlay";
            Controls.Add(value);
            value.BringToFront();
            WorkflowSurfaceBehavior.SetMinimapOverlayName(this, "PART_MinimapOverlay");
        }
    }

    /// <summary>
    /// A selector that builds the node and link views instead of the two virtual factories.
    /// </summary>
    /// <remarks>
    /// Setting this replaces <see cref="CreateNodeView"/> / <see cref="CreateLinkView"/>. The surface still records
    /// each view's role from the item it was created for, so layering and pan placement keep working.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowTemplateSelector? TemplateSelector
    {
        get => _templateSelector;
        set
        {
            if (ReferenceEquals(_templateSelector, value)) return;

            _templateSelector = value;
            _poolSelector = new ViewFactorySelector(this, value);

            if (_tree is not null)
            {
                ViewPool.SetTemplateSelector(PART_Canvas, _poolSelector);
            }
        }
    }

    /// <summary>
    /// The link interaction hub for the currently bound tree — the same instance Core gives every other surface
    /// over that tree (see <see cref="VeloxDev.WorkflowSystem.LinkInteraction.For"/>); <see langword="null"/>
    /// until a tree is bound.
    /// </summary>
    /// <remarks>
    /// The hub already turns the hover into a highlight and performs the Delete request, so a host only
    /// subscribes here to add a policy of its own (a context menu, say). The surface's own contribution is
    /// platform input translation plus taking keyboard focus when the hover changes. The hit test walks the
    /// curves the pooled <see cref="WorkflowLinkView"/>s published, in the canvas-local space the pointer is
    /// translated to.
    /// </remarks>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public LinkInteraction? LinkInteraction => _linkInteraction;

    /// <summary>Surface background, behind the grid and the cards.</summary>
    public Color SurfaceBackground
    {
        get => _surfaceBackground;
        set
        {
            if (_surfaceBackground == value) return;
            _surfaceBackground = value;
            BackColor = value;
            PART_ScrollViewer.BackColor = value;
            PART_Canvas.Invalidate();
        }
    }

    /// <summary>Chrome border colour, drawn as a rounded outline around the surface.</summary>
    public Color SurfaceBorderBrush
    {
        get => _surfaceBorderBrush;
        set
        {
            if (_surfaceBorderBrush == value) return;
            _surfaceBorderBrush = value;
            Invalidate();
        }
    }

    /// <summary>Chrome border thickness, in pixels; also the padding the content is inset by.</summary>
    public int SurfaceBorderThickness
    {
        get => _surfaceBorderThickness;
        set
        {
            if (_surfaceBorderThickness == value) return;
            _surfaceBorderThickness = value;
            Padding = new Padding(value);
            Invalidate();
        }
    }

    /// <summary>Chrome corner radius, in pixels.</summary>
    public int SurfaceCornerRadius
    {
        get => _surfaceCornerRadius;
        set
        {
            if (_surfaceCornerRadius == value) return;
            _surfaceCornerRadius = value;
            Invalidate();
        }
    }

    /// <summary>Creates the card for a node.</summary>
    /// <param name="node">The node to render.</param>
    /// <returns>The card. It should implement <see cref="IWorkflowSurfaceNodeView"/> so the surface can place it.</returns>
    protected abstract Control CreateNodeView(IWorkflowNodeViewModel node);

    /// <summary>Creates the view for a link.</summary>
    /// <param name="link">The link to render.</param>
    /// <returns>The view.</returns>
    protected abstract Control CreateLinkView(IWorkflowLinkViewModel link);

    /// <summary>Called after a tree has been bound and its views pooled — a host's chance to bind its own overlays.</summary>
    /// <param name="tree">The tree, or <see langword="null"/> when one was unbound.</param>
    protected virtual void OnTreeAttached(IWorkflowTreeViewModel? tree)
    {
    }

    /// <summary>
    /// Called after the surface has recomputed what it shows — a pan, a zoom or a layout pass.
    /// </summary>
    /// <remarks>
    /// For overlays that render the projection rather than the content, so they stay current between the
    /// surface behaviour's own refresh cycles.
    /// </remarks>
    protected virtual void OnSurfaceRefreshed()
    {
    }

    /// <summary>Called before a connection is made between two of the bound tree's ports.</summary>
    /// <param name="e">The two ports the connection would join.</param>
    /// <remarks>Set <see cref="WorkflowEventHandle.PreventDefault"/> on the argument's handle to cancel this one drag.</remarks>
    protected virtual void OnConnecting(ConnectionEventArgs e)
    {
    }

    /// <summary>Called once a connection was made and the link exists.</summary>
    /// <param name="e">The two ports the connection joined.</param>
    protected virtual void OnConnected(ConnectionEventArgs e)
    {
    }

    /// <summary>
    /// Fills the context menu the surface shows for a link. The base adds a single <c>Delete</c> item; override it
    /// to add or remove entries.
    /// </summary>
    /// <param name="menu">The menu being built; the surface shows it once this returns.</param>
    /// <param name="link">The link the menu is about.</param>
    /// <remarks>
    /// The menu is built anew for every right press, so an edit here takes effect the next time it opens. The
    /// surface opens it only when a link was hit; a right press on empty canvas raises the request with no link
    /// and is not shown.
    /// </remarks>
    protected virtual void OnBuildLinkMenu(ContextMenuStrip menu, IWorkflowLinkViewModel link)
    {
        menu.Items.Add("Delete", null, (_, _) => link.DeleteCommand.Execute(null));
    }

    /// <summary>Parses a <c>#RRGGBB</c>, <c>#AARRGGBB</c> or named colour.</summary>
    /// <param name="hex">The colour text.</param>
    /// <returns>The colour.</returns>
    protected static Color ParseColor(string hex) => WorkflowSurfaceColors.Parse(hex);

    private RulerOverlayForm? _rulerOverlay;

    private Color _surfaceBackground = Color.FromArgb(0x1E, 0x1E, 0x1E);
    private Color _surfaceBorderBrush = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
    private int _surfaceBorderThickness = 1;
    private int _surfaceCornerRadius = 3;

    private IWorkflowTreeViewModel? _tree;
    private INotifyPropertyChanged? _notifier;
    private IDisposable? _modelEvents;
    private readonly IWorkflowTreeEventSink _eventSink;
    private IWorkflowTemplateSelector? _templateSelector;
    private ViewFactorySelector _poolSelector;
    private bool _layoutPending;

    // 连线交互 hub 归 Core（每棵树一个，见 LinkInteraction.For）：本家只做平台的两件事 —— 翻译指针/按键、
    // 悬停命中时给画布取键盘焦点。高亮与删除都由 hub 的 AutoHighlight / AutoDelete 负责。
    private LinkInteraction? _linkInteraction;

    // 当前弹出的连线菜单。每次右键现建、收起即弃 —— 复用一份会带着上一次那条链接的捕获。
    private ContextMenuStrip? _linkMenu;

    // 当前这份菜单指着的那条线：hub 报「这条线离树了」时用它认领是不是自己这份菜单，认领了才收。
    private IWorkflowLinkViewModel? _menuLink;

    // 最近一次指针位置与它是否还在画布上：平移/缩放挪的是几何而指针没动，命中会变，得拿这两个值重判。
    private Point _lastPointerClient;
    private bool _pointerInside;

    // 平移状态。画布固定盖住视口，平移表现为每个卡片的世界原点位移 —— 见 ApplyPan。
    private bool _isPanning;
    private Point _panPressScreen;
    private Point _panOffsetAtPress;

    // 世界原点的有符号平移量。静止时为 (0,0)：原点由 Layout.ActualOffset 单独决定，与 XAML 那几家一致
    // （它们把标尺带折进独立的 render transform，而不是折进 pan）。网格画在 value - worldLeft、卡片画在
    // anchor + panOffset + ActualOffset，所以两者一起动、始终对齐。
    private Point _panOffset = new(0, 0);

    // 池绑定的可见集（每个可见节点/连线一个视图，含手势里的虚拟连线）。只挂 z 序：池会把它生成或回收的
    // 每个视图提到最前，不压的话最新生成的连线会盖在卡片上。
    private ObservableCollection<IWorkflowViewModel>? _visibleItems;

    // 每个由选择器造出来的视图属于哪一层。用弱表：池会回收视图，强引用会随换树累积。
    private readonly ConditionalWeakTable<Control, RoleBox> _roles = new();

    /// <summary>Creates the surface and wires the attached behaviours.</summary>
    protected WorkflowTreeView()
    {
        _eventSink = new TreeEventSink(this);
        DoubleBuffered = true;
        BackColor = _surfaceBackground;

        // 表面外框在 OnPaintBackground 里画（圆角边框），子控件按边框厚度内缩。
        Padding = new Padding(_surfaceBorderThickness);
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        // 滚动宿主：只当视口用。平移直接改画布（见 ApplyPan），所以 AutoScroll 关掉 —— 它自己的滚动位置
        // 被夹在 >= 0，只会允许往右下平移。背景不透明：WinForms 没有可靠的透明合成，画布自己画网格。
        PART_ScrollViewer = new ScrollableControl
        {
            Dock = DockStyle.Fill,
            AutoScroll = false,
            BackColor = _surfaceBackground,
            Name = "PART_ScrollViewer",
        };

        var canvas = new SurfaceCanvas(this)
        {
            Dock = DockStyle.Fill,
            Location = Point.Empty,
            Name = "PART_Canvas",
        };
        PART_Canvas = canvas;
        PART_ScrollViewer.Controls.Add(PART_Canvas);
        Controls.Add(PART_ScrollViewer);

        _poolSelector = new ViewFactorySelector(this, null);

        WorkflowSurfaceBehavior.SetIsEnabled(this, true);
        WorkflowSurfaceBehavior.SetZoomEnabled(this, true);
        WorkflowSurfaceBehavior.SetScrollViewerName(this, "PART_ScrollViewer");
        WorkflowSurfaceBehavior.SetCanvasName(this, "PART_Canvas");
        // 画布自己画网格与标尺，所以它同时就是网格装饰器。
        WorkflowSurfaceBehavior.SetGridDecoratorName(this, "PART_Canvas");
        WorkflowSurfaceBehavior.SetPointerPressSourceName(this, "PART_Canvas");

        // 平移：拖空白画布按有符号偏移移动表面。卡片和插槽是画布的子控件、有自己的鼠标事件，所以这几个
        // 处理器只在拖背景时跑。
        PART_Canvas.MouseDown += OnCanvasMouseDown;
        PART_Canvas.MouseMove += OnCanvasMouseMove;
        PART_Canvas.MouseUp += OnCanvasMouseUp;
        PART_Canvas.MouseCaptureChanged += OnCanvasMouseCaptureChanged;
        PART_Canvas.MouseEnter += OnCanvasMouseEnter;
        PART_Canvas.MouseLeave += OnCanvasMouseLeave;
        PART_Canvas.KeyDown += OnCanvasKeyDown;

        HandleCreated += OnHandleCreated;
        Resize += OnSurfaceResize;
    }

    // 记录视图的角色。基类不能按类型判断用户的视图，但造它的那一刻就知道 item 是什么 —— 这份记录同时
    // 供 z 序（连线沉底）与平移循环（卡片重摆）使用。
    private void RecordRole(Control view, object item)
    {
        var box = _roles.GetOrCreateValue(view);
        box.IsLink = item is IWorkflowLinkViewModel;
        box.IsNode = item is IWorkflowNodeViewModel;
    }

    // 拖小地图视口请求滚动：把想要的世界原点反解成有符号平移量（ApplyPan 会把 -panOffset 推回小地图，
    // 于是视口跟着拖）。
    private void OnMinimapScrollRequested(double sx, double sy)
    {
        _panOffset = new Point((int)Math.Round(-sx), (int)Math.Round(-sy));
        ApplyPan();
    }

    private void AttachTree()
    {
        // 学到所管的树这一处接上模型事件；先摘掉上一份订阅再按新树接。
        _modelEvents?.Dispose();
        _modelEvents = _tree is null ? null : WorkflowEventRelay.Attach(_tree, _eventSink);

        WorkflowSurfaceBehavior.SetWorkflowTree(this, _tree);

        // 重新配置池（先摘掉上一个管理器再挂上）。条目源是树的可见集，由 ApplyPan 经 helper.Viewport 保持
        // 最新，于是池为每个可见节点与连线物化/回收一个视图。
        var visibleItems = _tree?.GetHelper().VisibleItems;
        ViewPool.SetItemsSource(PART_Canvas, visibleItems);
        ViewPool.SetTemplateSelector(PART_Canvas, _poolSelector);

        // 挂在池之后，所以这个钩子一定跑在管理器应用完变更之后 —— 见 ArrangeLinkViews。
        AttachVisibleItems(visibleItems);
        ArrangeLinkViews();

        if (_tree is not null && PART_MinimapOverlay is not null)
        {
            WorkflowSurfaceBehavior.Refresh(this);
        }

        AttachLinkInteraction();
    }

    private void AttachVisibleItems(ObservableCollection<IWorkflowViewModel>? items)
    {
        // 即使是同一个集合也要重新订阅：钩子必须留在池自己的处理器之后，而重新挂载会把池那个也重新注册。
        if (_visibleItems is not null)
        {
            _visibleItems.CollectionChanged -= OnVisibleItemsChanged;
        }

        _visibleItems = items;
        if (items is not null)
        {
            items.CollectionChanged += OnVisibleItemsChanged;
        }
    }

    private void OnVisibleItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ArrangeLinkViews();

    // 把连线压到画布最底层，否则池新生成的连线会盖在卡片上。
    private void ArrangeLinkViews()
    {
        // 先快照：改序会改动子控件集合。
        foreach (var control in PART_Canvas.Controls.OfType<Control>().ToArray())
        {
            if (_roles.TryGetValue(control, out var role) && role.IsLink)
            {
                control.SendToBack();
            }
        }
    }

    private void OnTreeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowTreeViewModel.Layout)
            or nameof(IWorkflowTreeViewModel.Nodes)
            or nameof(IWorkflowTreeViewModel.Links))
        {
            ScheduleLayout();
        }
    }

    private void OnHandleCreated(object? sender, EventArgs e)
    {
        HandleCreated -= OnHandleCreated;
        if (Tag is IWorkflowTreeViewModel tagged)
        {
            ViewModel = tagged;
        }

        EnsureRulerOverlay();
        ScheduleLayout();
    }

    // 表面挂到窗体上之后才建浮层 —— FindForm 需要顶层窗口。OnHandleCreated 与 ApplyPan 都会惰性调用它，
    // 所以在控件加进窗体之前就绑好树也能拿到浮层。
    private void EnsureRulerOverlay()
    {
        if (_rulerOverlay is not null || IsDisposed || !IsHandleCreated) return;
        var owner = FindForm();
        if (owner is null || owner.IsDisposed) return;

        _rulerOverlay = new RulerOverlayForm { RulerThickness = SurfaceCanvas.DefaultRulerThickness };
        _rulerOverlay.Show(owner);                     // 有主弹窗：在宿主之上、跟着搬
        SyncRulerOverlay();

        PART_ScrollViewer.Resize += OnRulerOverlayHostChanged;
        PART_ScrollViewer.LocationChanged += OnRulerOverlayHostChanged;
        owner.Move += OnRulerOverlayOwnerMoved;        // 有主窗口自动跟着搬，但要在新矩形上重画
        owner.Resize += OnRulerOverlayHostChanged;     // 停靠的视口随窗口改变大小
    }

    // 把浮层对准视口并重画。
    private void SyncRulerOverlay()
    {
        if (_rulerOverlay is null || _rulerOverlay.IsDisposed) return;
        if (PART_ScrollViewer.Width < 1 || PART_ScrollViewer.Height < 1) return;

        _rulerOverlay.Location = PART_ScrollViewer.PointToScreen(Point.Empty);
        _rulerOverlay.Size = PART_ScrollViewer.ClientSize;
        _rulerOverlay.RefreshSurface();
    }

    private void OnRulerOverlayHostChanged(object? sender, EventArgs e) => SyncRulerOverlay();
    private void OnRulerOverlayOwnerMoved(object? sender, EventArgs e) => SyncRulerOverlay();

    private void OnSurfaceResize(object? sender, EventArgs e) => ScheduleLayout();

    private void OnCanvasMouseDown(object? sender, MouseEventArgs e)
    {
        // 右键对连线有意义：转发给交互后由宿主决定弹不弹菜单。空白处右键不启动平移。
        if (e.Button == MouseButtons.Right)
        {
            PublishPointer(PointerPhase.Pressed, e.Location, PointerButtonKind.Right);
            return;
        }

        if (e.Button != MouseButtons.Left || _isPanning) return;

        PublishPointer(PointerPhase.Pressed, e.Location, PointerButtonKind.Left);

        _isPanning = true;
        _panPressScreen = Cursor.Position;
        _panOffsetAtPress = _panOffset;
        PART_Canvas.Capture = true;
    }

    private void OnCanvasMouseMove(object? sender, MouseEventArgs e)
    {
        _lastPointerClient = e.Location;

        if (_isPanning)
        {
            var current = Cursor.Position;
            _panOffset = new Point(
                _panOffsetAtPress.X + (current.X - _panPressScreen.X),
                _panOffsetAtPress.Y + (current.Y - _panPressScreen.Y));
            ApplyPan();
            return;
        }

        // 悬停即选中：命中由 Core 裁决，这里只把指针位置翻译过去。
        PublishPointer(PointerPhase.Moved, e.Location);
    }

    private void OnCanvasMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_isPanning) return;

        _isPanning = false;
        PART_Canvas.Capture = false;
        ApplyPan();
    }

    private void OnCanvasMouseCaptureChanged(object? sender, EventArgs e)
    {
        // 捕获被抢走或在画布外释放（如 alt-tab）：停止平移，下次按下重新开始拖。
        if (!PART_Canvas.Capture)
        {
            _isPanning = false;
        }
    }

    private void OnCanvasMouseEnter(object? sender, EventArgs e) => _pointerInside = true;

    private void OnCanvasMouseLeave(object? sender, EventArgs e)
    {
        _pointerInside = false;
        _linkInteraction?.Publish(new PointerEvent(PointerPhase.Exited, new Anchor()));
    }

    // 键也过 Core：「现在按 Delete 删哪条」因此与其它六家是同一个答案，不靠各家各记一个选中。
    // 只处理 Delete：这块画布只有在悬停选中它时才拿得到焦点，落在别处的键不受影响。
    private void OnCanvasKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Delete) return;
        if (_linkInteraction?.HoveredLink is null) return;

        _linkInteraction.Publish(new KeyEvent(InputKey.Delete));
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    // 画布客户区坐标就是 slot.Anchor 的空间（见 WorkflowSlotLayoutBehavior 的坐标宿主），与发布的曲线同系。
    private void PublishPointer(PointerPhase phase, Point client, PointerButtonKind button = PointerButtonKind.None)
        => _linkInteraction?.Publish(new PointerEvent(phase, new Anchor(client.X, client.Y, 0), button));

    // 平移/缩放把几何挪到了指针之外或之下：指针没动，命中却变了，拿最近一次位置重判一次。
    private void RefreshLinkHover()
    {
        if (!_pointerInside || _linkInteraction is null) return;

        PublishPointer(PointerPhase.Moved, _lastPointerClient);
    }

    // 交互归 Core：本家只做平台的事 —— 把画布的指针/按键翻译成标准输入事件转发进去，命中时给画布取焦点。
    // 高亮（AutoHighlight）与删除（AutoDelete）由 hub 自己完成，本家不再各自实现一份。
    private void AttachLinkInteraction()
    {
        DetachLinkInteraction();
        if (_tree is null) return;

        // hub 由 Core 按树缓存：这里取到的就是同一棵树在任意界面上的那一个，本家不再自己造实例。
        var interaction = VeloxDev.WorkflowSystem.LinkInteraction.For(_tree);
        interaction.HoverChanged += OnLinkHoverChanged;
        // 菜单由基类弹（条目见 OnBuildLinkMenu）。宿主想否决某一次，订 ContextMenuRequesting（Preview 相）即可 ——
        // 它在 Requested 之前由 Core 按构造顺序发出，与这里的订阅先后无关。
        interaction.ContextMenuRequested += OnContextMenuRequested;
        // 菜单指着的那条线离树时，hub 发这个；宿主收不了自己的弹窗，只负责把它关掉。
        interaction.ContextMenuDismissRequested += OnContextMenuDismissRequested;
        _linkInteraction = interaction;
    }

    private void DetachLinkInteraction()
    {
        if (_linkInteraction is null) return;

        _linkInteraction.HoverChanged -= OnLinkHoverChanged;
        _linkInteraction.ContextMenuRequested -= OnContextMenuRequested;
        _linkInteraction.ContextMenuDismissRequested -= OnContextMenuDismissRequested;

        // 会话结束菜单还挂着的话先收起：Closed 会顺手把 hub 的挂起放开，换一棵树时不至于一直停在不接收移动。
        _linkMenu?.Close();

        // 解绑前清掉悬停：hub 跟着树活着，比这次绑定久 —— 不清的话换一棵树、或重新绑同一棵时，
        // 上一条线还亮着。Exited 不走 IsSuspended，一定生效；AutoHighlight 顺手把高亮熄灭。
        _linkInteraction.Publish(new PointerEvent(PointerPhase.Exited, new Anchor()));
        _linkInteraction = null;
    }

    // 高亮由 hub 经 ILinkHighlight 写到命中那条线的控件上（池化的 WorkflowLinkView 实现了它）；
    // 本家在这里只补平台欠的那件事：命中时给画布键盘焦点，Delete 才进得来（同其它六家）。
    private void OnLinkHoverChanged(object? sender, LinkHoverEventArgs e)
    {
        if (e.Link is null || !PART_Canvas.CanFocus) return;
        PART_Canvas.Focus();
    }

    // 右键菜单归表面、不归连线视图：右键落在表面上（别家的连线视图甚至不吃指针），而弹出要屏幕坐标、
    // 模型给的是画布坐标 —— 只有表面同时知道这两件事。条目由 OnBuildLinkMenu 给出（基类默认一项 Delete）。
    private void OnContextMenuRequested(object? sender, ContextMenuRequestedEventArgs e)
    {
        // 宿主的否决走 ContextMenuRequesting，那是 Preview 相：被否决时这个事件根本不会发出。这里不再读句柄。
        if (e.Link is not { } link) return;
        if (_linkMenu?.Visible == true) return;

        var menu = new ContextMenuStrip();
        OnBuildLinkMenu(menu, link);

        // e.Position 是画布客户区坐标（指针位置的发布就是原样转发的 PART_Canvas 客户区坐标），换成屏幕坐标再弹。
        var screen = PART_Canvas.PointToScreen(
            new Point((int)e.Position.Horizontal, (int)e.Position.Vertical));

        menu.Closed += (_, _) =>
        {
            // 收起报回 hub：它自己放开 IsSuspended，宿主不用记这一笔账。
            _linkInteraction?.Publish(new ContextMenuEvent(ContextMenuPhase.Closed, e.Position, link));
            if (ReferenceEquals(_linkMenu, menu))
            {
                _linkMenu = null;
                _menuLink = null;
            }
            // 关掉即弃，但不在 Closed 里直接 Dispose —— 那还在菜单自己的方法里，销毁要在它收完尾之后。
            if (!IsDisposed) BeginInvoke(new Action(menu.Dispose));
        };

        _linkMenu = menu;
        _menuLink = link;

        // 菜单一开指针就飞到菜单上去：先报 Opened，hub 把悬停挂起，那之后的移动不会清掉这次选中的线。
        _linkInteraction?.Publish(new ContextMenuEvent(ContextMenuPhase.Opened, e.Position, link));
        menu.Show(screen);
    }

    // 菜单指着的那条线已经不在树上：hub 请宿主收起这份菜单（它收不了宿主的弹窗）。收起照常报 Closed，挂起随之放开。
    private void OnContextMenuDismissRequested(object? sender, ContextMenuDismissRequestedEventArgs e)
    {
        if (!ReferenceEquals(_menuLink, e.Link)) return;
        _linkMenu?.Close();
    }

    // 应用有符号平移量：重摆卡片，把得到的世界原点推给网格、浮层、小地图与树的视口。
    // 表面行为只在布局周期里刷新这些，所以平移要显式推一次，标尺/网格与小地图视口才跟得上。
    private void ApplyPan()
    {
        if (IsDisposed) return;

        // 绝不在左/上边缘上报负的画布视口（scroll < 0），与 XAML 那几家一致：任何会越过原点的平移都被
        // 折进 NegativeOffset 的增长（pan + ActualOffset 保持不变，所以看不出东西动）。于是静止时
        // 「scroll offset = 0」，只有真的越过边缘才向左/上长出世界。
        if (_tree?.Layout is { } layout && (_panOffset.X > 0 || _panOffset.Y > 0))
        {
            int growX = Math.Max(0, _panOffset.X);
            int growY = Math.Max(0, _panOffset.Y);
            layout.NegativeOffset += new Offset(growX, growY);
            // 把拖拽基点按折进去的量重新落位，使同一个鼠标位置对应 panOffset 0；否则每次在同一绝对增量上
            // 重入 ApplyPan 都会再折一次，平移会失控。
            _panOffsetAtPress = new Point(_panOffsetAtPress.X - growX, _panOffsetAtPress.Y - growY);
            _panOffset = new Point(_panOffset.X - growX, _panOffset.Y - growY);
        }

        // 浮层需要表面已经挂到窗体上（FindForm），所以这里也惰性建一次 —— 在 OnHandleCreated 之前平移也能用。
        EnsureRulerOverlay();

        // 画布固定盖住视口，平移是对每个卡片的世界原点位移。先把新值发布到画布上（后加进来的卡片要靠它），
        // 再重摆已有卡片，然后重算插槽锚点让连线跟上（布局行为按插槽的屏幕位置算锚点，而卡片刚动过）。
        // 这里同步重摆，每个 ApplyPosition 也同步重测自己的插槽（SyncNow），所以下面那次同步重画之前
        // 锚点与连线窗口都是新的。
        var content = new Offset(
            _tree?.Layout?.ActualOffset.Horizontal ?? 0,
            _tree?.Layout?.ActualOffset.Vertical ?? 0);

        // 视觉内容原点 = ActualOffset + 标尺预留（纯屏幕空间的位移，让世界轴落进标尺内角）。下面那份
        // helper.Viewport 仍只用 content，所以对外报出的数字与其它适配器完全一致。
        var contentVisual = new Offset(content.Horizontal + RulerReserve, content.Vertical + RulerReserve);

        ((SurfaceCanvas)PART_Canvas).PanOffset = _panOffset;
        foreach (var child in PART_Canvas.Controls.OfType<Control>())
        {
            if (child is IWorkflowSurfaceNodeView nodeView)
            {
                nodeView.ApplySurfacePosition(_panOffset, contentVisual);
                WorkflowSlotLayoutBehavior.SyncNow(child);
            }
        }

        if (PART_GridDecorator is IWorkflowGridDecorator grid)
        {
            grid.ScrollOffsetX = -_panOffset.X;
            grid.ScrollOffsetY = -_panOffset.Y;
            grid.ContentOffsetX = contentVisual.Horizontal;
            grid.ContentOffsetY = contentVisual.Vertical;
            PART_GridDecorator.Invalidate();
        }

        // 浮层读同一个世界原点，于是内容在视口固定的带下面滚动时，刻度与网格线一直对齐。
        if (_rulerOverlay is not null)
        {
            _rulerOverlay.ScrollOffsetX = -_panOffset.X;
            _rulerOverlay.ScrollOffsetY = -_panOffset.Y;
            _rulerOverlay.ContentOffsetX = contentVisual.Horizontal;
            _rulerOverlay.ContentOffsetY = contentVisual.Vertical;
            SyncRulerOverlay();
        }

        if (PART_MinimapOverlay is IWorkflowMinimapOverlay minimap)
        {
            minimap.ScrollOffsetX = -_panOffset.X;
            minimap.ScrollOffsetY = -_panOffset.Y;
            minimap.ContentOffsetX = contentVisual.Horizontal;
            minimap.ContentOffsetY = contentVisual.Vertical;
            minimap.ViewportWidth = PART_ScrollViewer.ClientSize.Width;
            minimap.ViewportHeight = PART_ScrollViewer.ClientSize.Height;
            PART_MinimapOverlay.Invalidate();
        }

        PART_Canvas.Invalidate();

        // Invalidate 只是排队；高频平移时 WM_PAINT 被推迟，旧卡片位置与旧网格来不及擦掉，会拖出残影。
        // 同步重画画布（网格）与装饰器，保证每帧干净。
        PART_Canvas.Update();
        PART_GridDecorator.Update();

        try
        {
            // 把浮动标尺带（RulerReserve）算进虚拟化：这个自绘表面自己驱动 Viewport，绕过了适配器里那层
            // 自动同步 inset 的封装。
            _tree?.SetVirtualizeInset(left: RulerReserve, top: RulerReserve);
            _tree?.GetHelper().Viewport = new Viewport(
                -_panOffset.X - content.Horizontal,
                -_panOffset.Y - content.Vertical,
                PART_ScrollViewer.ClientSize.Width,
                PART_ScrollViewer.ClientSize.Height);
        }
        catch
        {
            // 有些宿主上的树 helper 不支持写视口；忽略。
        }

        OnSurfaceRefreshed();
        RefreshLinkHover();
    }

    private void ScheduleLayout()
    {
        if (_layoutPending || IsDisposed) return;
        _layoutPending = true;

        Action update = () =>
        {
            _layoutPending = false;
            if (IsDisposed) return;
            ApplyCanvasSize();
            WorkflowSurfaceBehavior.Refresh(this);
            // 布局稳定后把当前平移量（有符号，静止为 0）推给画布、网格与小地图；ApplyPan 否则只在用户平移
            // 时被调用。每次都跑，所以改窗口大小也能让浮动标尺跟上。
            ApplyPan();
        };

        if (IsHandleCreated)
        {
            BeginInvoke(update);
        }
        else
        {
            _layoutPending = false;
        }
    }

    private void ApplyCanvasSize()
    {
        // 画布停靠填满视口、卡片按平移量位移，所以它永远盖住可见区 —— 没有内容尺寸的画布需要同步。
        // 只管住首次布局时的零尺寸（停靠会在下一轮布局里填上）。
        if (PART_Canvas.Width < 1 || PART_Canvas.Height < 1)
        {
            PART_Canvas.Size = PART_ScrollViewer.ClientSize;
        }
        // 注意：AutoScrollMinSize 是**故意不设**的 —— 赋它会触发 AdjustScrollbars、重新打开 AutoScroll，
        // 与手动平移打架。
    }

    /// <inheritdoc />
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (e is null) throw new ArgumentNullException(nameof(e));

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0, 0, Width, Height);
        using var path = WorkflowSurfaceGraphics.RoundedRectangle(bounds, _surfaceCornerRadius);
        using var brush = new SolidBrush(_surfaceBackground);
        using var pen = new Pen(_surfaceBorderBrush, _surfaceBorderThickness);
        g.FillPath(brush, path);
        g.DrawPath(pen, path);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DetachLinkInteraction();
            ViewPool.SetItemsSource(PART_Canvas, null);
            ViewPool.SetTemplateSelector(PART_Canvas, null);
            AttachVisibleItems(null);

            if (_notifier is not null)
            {
                _notifier.PropertyChanged -= OnTreeChanged;
                _notifier = null;
            }

            _modelEvents?.Dispose();
            _modelEvents = null;

            _linkMenu?.Dispose();
            _linkMenu = null;

            if (_rulerOverlay is not null)
            {
                _rulerOverlay.Dispose();
                _rulerOverlay = null;
            }
        }

        base.Dispose(disposing);
    }

    private sealed class RoleBox
    {
        public bool IsLink { get; set; }
        public bool IsNode { get; set; }
    }

    // 基类自己接模型事件、自己摘订阅，把每条转发进对应的可重写钩子；宿主只重写钩子，不碰 Helper。
    private sealed class TreeEventSink(WorkflowTreeView owner) : IWorkflowTreeEventSink
    {
        public void OnConnecting(ConnectionEventArgs e) => owner.OnConnecting(e);
        public void OnConnected(ConnectionEventArgs e) => owner.OnConnected(e);
    }

    // 把宿主给的两个工厂（或宿主自带的 selector）包起来，顺手记下每个视图的角色。
    private sealed class ViewFactorySelector : IWorkflowTemplateSelector
    {
        private readonly WorkflowTreeView _owner;
        private readonly IWorkflowTemplateSelector? _inner;

        public ViewFactorySelector(WorkflowTreeView owner, IWorkflowTemplateSelector? inner)
        {
            _owner = owner;
            _inner = inner;
        }

        public Control CreateView(object item)
        {
            var view = _inner is not null
                ? _inner.CreateView(item)
                : item switch
                {
                    IWorkflowNodeViewModel node => _owner.CreateNodeView(node),
                    IWorkflowLinkViewModel link => _owner.CreateLinkView(link),
                    _ => throw new InvalidOperationException(
                        $"Unsupported workflow item: {item?.GetType().FullName}"),
                };

            _owner.RecordRole(view, item);
            return view;
        }
    }

    // 卡片层：不透明表面，托管池化视图，并在 OnPaintBackground 里画网格 —— 卡片与连线是子控件，在它之后
    // 重画。整条链上没有任何透明：WinForms 没有可靠的透明合成，所以连线视图是按折线的描边带雕出窗口区域，
    // 而不是一个透明兄弟窗口。
    private sealed class SurfaceCanvas : Panel, IWorkflowGridDecorator
    {
        // 与完整 demo 的自绘画布共用这套度量。internal 让外层模板在初始化平移量时读到它。
        internal const double DefaultRulerThickness = 36;
        private const double GridSpacing = 40;
        private const int MajorFreq = 5;

        private readonly WorkflowTreeView _owner;
        private readonly Color _minorGridColor = ParseColor("#2A2D2E");
        private readonly Color _majorGridColor = ParseColor("#3A3D40");
        private readonly Color _axisColor = ParseColor("#4D4D4D");

        public SurfaceCanvas(WorkflowTreeView owner)
        {
            _owner = owner;
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint |
                ControlStyles.Selectable,
                true);
            // 悬停选中要能收 Delete：Panel 默认不可获焦，没有焦点键就进不来。TabStop 留 false —— 要的是
            // 「选中时拿得到焦点」，不是往制表位里塞一站。
            TabStop = false;
        }

        /// <summary>
        /// Current signed pan offset (world-origin translation).
        /// </summary>
        /// <remarks>
        /// Node views read this from their parent chain to place themselves at <c>node.Anchor + PanOffset</c> in
        /// canvas-local coordinates. Keeping the canvas fixed over the viewport and translating the cards — rather
        /// than moving the canvas — is what keeps every card of the visible world region inside the canvas window
        /// after panning into negative coordinates.
        /// </remarks>
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Point PanOffset { get; set; }

        /// <inheritdoc />
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double ScrollOffsetX { get; set; }

        /// <inheritdoc />
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double ScrollOffsetY { get; set; }

        /// <inheritdoc />
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double ContentOffsetX { get; set; }

        /// <inheritdoc />
        [Browsable(false)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public double ContentOffsetY { get; set; }

        /// <summary>Always zero here: the ruler is the floating overlay, not a band this canvas reserves.</summary>
        /// <remarks>
        /// Deliberately different from the standalone grid decorator, which reports its ruler thickness. Reporting
        /// a thickness here would cull nodes one ruler-band early, silently.
        /// </remarks>
        public double RulerBand => 0;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // 网格画在背景这一趟，正好落在卡片与雕孔连线带（子窗口）之下。浮动标尺带、刻度与标签由
            // RulerOverlayForm 画 —— 那是 WS_EX_LAYERED 的有主弹窗，按逐像素 alpha 合成在卡片**之上**，
            // 也是 WinForms 里唯一能真正压暗滚过带子的卡片的机制（与 WPF/Avalonia/WinUI/MAUI/Razor 一致）。
            // 网格整幅画（不按内容矩形裁），于是网格线从半透明带下面穿过、保持可见地变暗。
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0, 0, Width, Height);

            using var bgBrush = new SolidBrush(_owner.SurfaceBackground);
            g.FillRectangle(bgBrush, bounds);

            DrawGrid(g, bounds);
        }

        private void DrawGrid(Graphics g, RectangleF bounds)
        {
            var spacing = Math.Max(8, GridSpacing);
            var majorStep = spacing * Math.Max(1, MajorFreq);
            var worldLeft = WorkflowSurfaceMath.GridWorldLeft(ScrollOffsetX, ContentOffsetX);
            var worldTop = WorkflowSurfaceMath.GridWorldTop(ScrollOffsetY, ContentOffsetY);
            var worldRight = worldLeft + bounds.Width;
            var worldBottom = worldTop + bounds.Height;

            using var minorPen = new Pen(_minorGridColor, 1f);
            using var majorPen = new Pen(_majorGridColor, 1f);
            using var axisPen = new Pen(_axisColor, 1.2f);

            // 网格 x = value - worldLeft；卡片在 anchor + panOffset + ActualOffset，两者共用同一个世界原点，
            // 平移时保持对齐（静止时 pan = 0）。线画满视口，于是能伸到半透明带下面。
            var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
            for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
            {
                var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, 0);
                var pen = WorkflowSurfaceGrid.SelectPen(value, majorStep, minorPen, majorPen, axisPen);
                g.DrawLine(pen, x, 0, x, bounds.Height);
            }

            var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
            for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
            {
                var y = (float)WorkflowSurfaceMath.GridY(value, worldTop, 0);
                var pen = WorkflowSurfaceGrid.SelectPen(value, majorStep, minorPen, majorPen, axisPen);
                g.DrawLine(pen, 0, y, bounds.Width, y);
            }
        }
    }

    // 逐像素 alpha 的标尺带浮层。WinForms 的子控件永远画在父控件 OnPaintBackground 之上，所以画在那里的
    // 半透明带永远压不住从下面滚过的**不透明卡片** —— 卡片会盖住带子。唯一能压住的是 WS_EX_LAYERED 窗口：
    // 这是一个有主的、顶层、命中透明的弹窗，表面用 UpdateLayeredWindow 按逐像素 alpha 合成，于是带子真的
    // 合成在卡片之上（卡片在带下变暗，与其它五家一致）。分层**子**窗口在部分系统上会以
    // ERROR_NOT_SUPPORTED 失败，所以用有主弹窗；有主弹窗还会跟着宿主窗口移动、天然在其之上。
    // WS_EX_TOOLWINDOW 让它不进任务栏与 alt-tab；WM_NCHITTEST → HTTRANSPARENT 让带子下面的平移与节点拖拽
    // 照常工作。
    private sealed class RulerOverlayForm : Form
    {
        private const int WsExLayered = 0x00080000;
        private const int WsExNoActivate = 0x08000000;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExTransparent = 0x00000020;
        private const int HtTransparent = -1;
        private const int WmNcHitTest = 0x0084;
        private const uint UlwAlpha = 2;

        private const double GridSpacing = 40;
        private const int MajorFreq = 5;

        // 与表面同一套配色。alpha 取 0x70（WinForms 的偏离）：带子合成在卡片**和**网格之上，若按其它框架的
        // 0xC8，带下的网格只剩约 3.5 个亮度、看不出来；0x70 既留着色调，又让网格从带下可见地穿过。
        private readonly Color _rulerBackground = ParseColor("#70252526");
        private readonly Color _labelColor = ParseColor("#888888");
        private readonly Color _tickColor = ParseColor("#555555");
        private readonly Color _axisColor = ParseColor("#4D4D4D");
        private readonly Color _dividerColor = ParseColor("#3A3D40");
        private readonly Font _labelFont = new("Segoe UI", 13f, GraphicsUnit.Pixel);

        private Bitmap? _surface;

        public RulerOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            ControlBox = false;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WsExLayered | WsExNoActivate | WsExToolWindow | WsExTransparent;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation => true;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmNcHitTest)
            {
                m.Result = (IntPtr)HtTransparent;
                return;
            }

            base.WndProc(ref m);
        }

        public double RulerThickness { get; set; } = 36;

        public double ScrollOffsetX { get; set; }

        public double ScrollOffsetY { get; set; }

        public double ContentOffsetX { get; set; }

        public double ContentOffsetY { get; set; }

        // 重画分层表面。窗口自己的屏幕矩形就是位图尺寸；刻度与标签按逻辑（视口客户区）空间画、再缩放填满，
        // 于是 DPI 感知的宿主与虚拟化的宿主都与画布画的网格对齐。
        public void RefreshSurface()
        {
            if (IsDisposed || !IsHandleCreated || Width < 1 || Height < 1) return;

            GetWindowRect(Handle, out var rect);
            var pw = Math.Max(1, rect.Right - rect.Left);
            var ph = Math.Max(1, rect.Bottom - rect.Top);

            if (_surface is null || _surface.Width != pw || _surface.Height != ph)
            {
                _surface?.Dispose();
                _surface = new Bitmap(pw, ph, PixelFormat.Format32bppPArgb);
            }

            using (var g = Graphics.FromImage(_surface))
            {
                g.Clear(Color.Transparent);
                g.ScaleTransform(
                    pw / (float)Math.Max(1, Width),
                    ph / (float)Math.Max(1, Height));
                g.SmoothingMode = SmoothingMode.AntiAlias;
                DrawBands(g, Width, Height);
            }

            UpdateLayeredSurface(rect, pw, ph);
        }

        private void DrawBands(Graphics g, int cw, int ch)
        {
            var ruler = (float)RulerThickness;
            using var rulerBrush = new SolidBrush(_rulerBackground);
            using var dividerPen = new Pen(_dividerColor, 1f);
            using var tickPen = new Pen(_tickColor, 1f);
            using var axisPen = new Pen(_axisColor, 1f);
            using var labelBrush = new SolidBrush(_labelColor);
            using var format = new StringFormat(StringFormat.GenericTypographic);

            // 半透明带：一张位图，逐像素 alpha 压在下面的一切之上（画布的网格**和**不透明的卡片）。
            g.FillRectangle(rulerBrush, 0, 0, cw, ruler);
            g.FillRectangle(rulerBrush, 0, 0, ruler, ch);

            // 分隔线全不透明。
            g.DrawLine(dividerPen, ruler, 0, ruler, ch);
            g.DrawLine(dividerPen, 0, ruler, cw, ruler);

            var spacing = Math.Max(8, GridSpacing);
            var majorStep = spacing * Math.Max(1, MajorFreq);
            var worldLeft = WorkflowSurfaceMath.GridWorldLeft(ScrollOffsetX, ContentOffsetX);
            var worldTop = WorkflowSurfaceMath.GridWorldTop(ScrollOffsetY, ContentOffsetY);
            var worldRight = worldLeft + cw;
            var worldBottom = worldTop + ch;

            // 上标尺。刻度与网格共用 x = value - worldLeft（画布在同一下标处画网格线），所以内容在视口固定
            // 的带子下面滚动时刻度始终与网格线对齐。跳过 x < ruler，让转角与左侧带子保持干净。
            var firstVertical = WorkflowSurfaceMath.GridFirstLine(worldLeft, spacing);
            for (var value = firstVertical; value <= worldRight + spacing; value += spacing)
            {
                var x = (float)WorkflowSurfaceMath.GridX(value, worldLeft, 0);
                if (x < ruler)
                {
                    continue;
                }

                var isMajor = WorkflowSurfaceGrid.IsMajorLine(value, majorStep);
                var tickLength = isMajor ? (float)(ruler - 6) : Math.Max(6f, (float)(ruler * 0.35));
                var pen = WorkflowSurfaceGrid.IsNearZero(value) ? axisPen : tickPen;
                g.DrawLine(pen, x, ruler, x, (float)(ruler - tickLength));

                if (isMajor)
                {
                    g.DrawString(WorkflowSurfaceGrid.FormatGridValue(value), _labelFont, labelBrush, x + 3, 2, format);
                }
            }

            // 左标尺。
            var firstHorizontal = WorkflowSurfaceMath.GridFirstLine(worldTop, spacing);
            for (var value = firstHorizontal; value <= worldBottom + spacing; value += spacing)
            {
                var y = (float)WorkflowSurfaceMath.GridY(value, worldTop, 0);
                if (y < ruler)
                {
                    continue;
                }

                var isMajor = WorkflowSurfaceGrid.IsMajorLine(value, majorStep);
                var tickLength = isMajor ? (float)(ruler - 6) : Math.Max(6f, (float)(ruler * 0.35));
                var pen = WorkflowSurfaceGrid.IsNearZero(value) ? axisPen : tickPen;
                g.DrawLine(pen, ruler, y, (float)(ruler - tickLength), y);

                if (isMajor)
                {
                    g.DrawString(WorkflowSurfaceGrid.FormatGridValue(value), _labelFont, labelBrush, 3, y + 2, format);
                }
            }
        }

        private void UpdateLayeredSurface(RECT rect, int pw, int ph)
        {
            var screenDc = GetDC(IntPtr.Zero);
            var memDc = CreateCompatibleDC(screenDc);
            var hbitmap = _surface!.GetHbitmap(Color.FromArgb(0));
            var old = SelectObject(memDc, hbitmap);
            try
            {
                var ptDst = new POINT { X = rect.Left, Y = rect.Top };
                var size = new SIZE { cx = pw, cy = ph };
                var ptSrc = new POINT();
                var blend = new BLENDFUNCTION
                {
                    BlendOp = 0,               // AC_SRC_OVER
                    SourceConstantAlpha = 255,
                    AlphaFormat = 1,           // AC_SRC_ALPHA —— 位图必须是预乘的
                };
                UpdateLayeredWindow(Handle, screenDc, ref ptDst, ref size, memDc, ref ptSrc, 0, ref blend, UlwAlpha);
            }
            finally
            {
                SelectObject(memDc, old);
                DeleteObject(hbitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }


        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [DllImport("user32.dll")]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr ho);
    }
}
