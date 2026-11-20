using Demo.ViewModels;
using Demo.Workflow;
using System.ComponentModel;
using System.Windows.Forms;
using VeloxDev.TransitionSystem;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;
using Wf = VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// The workflow surface host. The heavy machinery — pan, zoom, viewport, node/link pooling, slot layout and the
/// floating ruler — belongs to <see cref="WorkflowBehaviors.WorkflowSurfaceBehavior"/>, attached in
/// <c>WorkflowCanvas.xaml</c>. What stays here is this demo's policy: session wiring, the link-flow clock, the
/// hover highlight, the delete gesture, the Ctrl-click selection veto and the node context menu.
/// </summary>
public sealed partial class WorkflowCanvas : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    // 连线的命中半径，交给输入路由用。6 是七家统一的值（Core 的 LinkHitTestEx.DefaultHitRadius）。
    private const float LinkHitRadius = 6f;

    // 一个周期：三段相位各自结束时头部走过的比例——出发、行进、到达。
    private const double BandFormed = 0.30;
    private const double BandLeaving = 0.78;

    private WorkflowDemoSession? _session;
    private WorkflowInput? _input;
    private double _nodeLayoutTolerance = 8d;

    // 彗星的两个数、整个表面共用：一条时钟给每条连线自己的色相。
    private double _bandHead;
    private double _bandIntensity;

    /// <summary>The session whose tree this surface renders.</summary>
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
    /// How far along every link the comet's head has travelled, as a fraction of that link's own arc length.
    /// </summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double BandHead
    {
        get => _bandHead;
        set { _bandHead = value; PushFlow(); }
    }

    /// <summary>How lit the comet is: 0 while it is absent, 1 while it travels.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double BandIntensity
    {
        get => _bandIntensity;
        set { _bandIntensity = value; PushFlow(); }
    }

    // 三段相位依次声明、Repeat(int.MaxValue) 整链重复并重放首周期抓到的起值；首尾皆无光，接缝不可见。
    private static readonly Transition<WorkflowCanvas> Flow =
        Transition<WorkflowCanvas>.Create()
            .Property(c => c.BandHead, BandFormed)
            .Property(c => c.BandIntensity, 1d)
            .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(450), Ease = Eases.Default, FPS = 30 })
            .Then()
            .Property(c => c.BandHead, BandLeaving)
            .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(700), Ease = Eases.Default, FPS = 30 })
            .Then()
            .Property(c => c.BandHead, 1d)
            .Property(c => c.BandIntensity, 0d)
            .Effect(new TransitionEffect { Duration = TimeSpan.FromMilliseconds(450), Ease = Eases.Default, FPS = 30 })
            .Repeat(int.MaxValue);

    /// <summary>Creates the surface and wires this demo's link menu, HUD and node menu.</summary>
    public WorkflowCanvas()
    {
        InitializeComponent();

        // Panel 默认不可获焦，而没有焦点就收不到 Delete：选中连线时要把焦点取过来。
        SetStyle(ControlStyles.Selectable, true);
        TabStop = false;

        WorkflowBehaviors.WorkflowSurfaceBehavior.SetLinkMenuBuilder(
            this, static (menu, link) => menu.Items.Add(
                "Delete", null, (_, _) => link.DeleteCommand.Execute(null)));

        WorkflowNodeCard.MenuBuilder = BuildNodeLayoutMenu;

        // WinForms 把子控件追加到 z 序末尾（最下）；把浮层提上来。
        minimapOverlay.BringToFront();
        _infoOverlay.BringToFront();

        // 视口数是 surface 在平移/缩放后推的，HUD 随画布每帧重算一次即可。
        surfaceCanvas.Paint += (_, _) => _infoOverlay.UpdateText();
    }

    /// <inheritdoc />
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        StartLinkFlow();
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 关掉表面行为：它会摘掉应用级过滤器并把树上的 Scroller 还原。
            WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(this, false);

            if (_session?.Tree.ResetVirtualLinkCommand.CanExecute(null) == true)
            {
                _session.Tree.ResetVirtualLinkCommand.Execute(null);
            }

            DetachSession(_session);
            WorkflowNodeCard.MenuBuilder = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>The minimap part declared in the surface markup, for host-level show/hide policy.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MinimapOverlay MinimapOverlay => minimapOverlay;

    // ── Session lifecycle ───────────────────────────────────────────────────────

    private void AttachSession(WorkflowDemoSession? session)
    {
        if (session is null) return;

        WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(this, session.Tree);
        AttachLinkInput(session.Tree);
        VetoFrameworkGestures(session.Tree);
        _infoOverlay.Bind(session.Tree);
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
        StartLinkFlow();
    }

    private void DetachSession(WorkflowDemoSession? session)
    {
        if (session is null) return;

        StopLinkFlow();
        DetachLinkInput();
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(this, null);
        _infoOverlay.Bind(null);
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(this);
    }

    // ── Link flow clock ─────────────────────────────────────────────────────────

    private void StartLinkFlow()
    {
        if (_session is null || !IsHandleCreated) return;

        _bandHead = 0d;
        _bandIntensity = 0d;
        Flow.Execute(this);
    }

    private void StopLinkFlow() => Transition.Exit(this, IncludeMutual: true, IncludeNoMutual: true);

    private void PushFlow()
    {
        WorkflowBehaviors.WorkflowLinkLayer.SetFlowHead(surfaceCanvas, _bandHead);
        WorkflowBehaviors.WorkflowLinkLayer.SetFlowIntensity(surfaceCanvas, _bandIntensity);
    }

    // ── Link input: hover highlight and delete ──────────────────────────────────

    private void AttachLinkInput(IWorkflowTreeViewModel tree)
    {
        DetachLinkInput();

        var input = WorkflowInput.For(tree);
        input.HitRadius = LinkHitRadius;
        _input = input;

        if (tree.GetHelper() is Wf.IInputEvents events)
        {
            events.Input.PointerMoved += OnInputPointerMoved;
            events.Input.PointerEntered += OnInputPointerMoved;
            events.Input.PointerExited += OnInputPointerExited;
            events.Input.KeyDown += OnInputKeyDown;
            events.Input.PointerWheelChanged += OnInputPointerWheel;
        }
    }

    private void DetachLinkInput()
    {
        if (_input is not { } input) return;

        if (input.Tree.GetHelper() is Wf.IInputEvents events)
        {
            events.Input.PointerMoved -= OnInputPointerMoved;
            events.Input.PointerEntered -= OnInputPointerMoved;
            events.Input.PointerExited -= OnInputPointerExited;
            events.Input.KeyDown -= OnInputKeyDown;
            events.Input.PointerWheelChanged -= OnInputPointerWheel;
        }

        // 解绑前清掉指针目标：路由跟着树活着，不清会留一条亮线。
        input.Route(new Wf.PointerExitedEventArgs(
            new Anchor(), Wf.InputModifiers.None, this, null, new WorkflowEventHandle()));
        _input = null;
    }

    private void OnInputPointerMoved(object? sender, Wf.PointerEventArgs e) => UpdateLinkHighlights();

    private void OnInputPointerExited(object? sender, Wf.PointerExitedEventArgs e) => UpdateLinkHighlights();

    // 单层绘制从 WorkflowInput.HoveredLink 读悬停线；指针换目标时重画画布就够了。
    private void UpdateLinkHighlights() => surfaceCanvas.Invalidate();

    // 键也过输入路由：**删不删是这里的决定**。更靠前的一级（连线自己）可以在句柄上拒绝这一次。
    private void OnInputKeyDown(object? sender, Wf.KeyDownEventArgs e)
    {
        if (_input is not { } input) return;

        if (e.Key == Wf.InputKey.Delete)
        {
            if (input.HoveredLink is not { } hovered || e.Handle.PreventDefault) return;
            if (hovered.DeleteCommand.CanExecute(null)) hovered.DeleteCommand.Execute(null);
            e.Handle.PreventDefault = true;
            return;
        }

        if (!e.Modifiers.HasFlag(Wf.InputModifiers.Control)) return;

        var redo = e.Modifiers.HasFlag(Wf.InputModifiers.Shift);
        if (e.Key == Wf.InputKey.Z && !redo) input.Tree.UndoCommand.Execute(null);
        else if (e.Key == Wf.InputKey.Y || (e.Key == Wf.InputKey.Z && redo)) input.Tree.RedoCommand.Execute(null);
        else return;

        e.Handle.PreventDefault = true;
    }

    // 画布滚轮由适配器整笔接管：按住 Shift 时拦下这一笔，改从横轴滚。
    // 竖轴正 = 远离用户，塞进横轴要取反 —— 横轴正 = 向右，而「远离用户」在画面上是向左。
    private void OnInputPointerWheel(object? sender, Wf.PointerWheelEventArgs e)
    {
        if (!e.Modifiers.HasFlag(Wf.InputModifiers.Shift) || _input is not { } input) return;

        e.Handle.PreventDefault = true;
        input.Scroller?.ScrollBy(-e.DeltaY, 0d);
    }

    /// <summary>
    /// The presses this host has claimed: Ctrl-click toggles agent selection, Shift-drag on the empty canvas is
    /// reserved for the host. The framework's own hand stands down when this refuses a press.
    /// </summary>
    private void VetoFrameworkGestures(TreeViewModel tree)
    {
        if (tree.GetHelper() is Wf.IInputEvents events)
        {
            events.Input.PointerPressed -= OnInputPointerPressed;
            events.Input.PointerPressed += OnInputPointerPressed;
        }
    }

    private static void OnInputPointerPressed(object? sender, Wf.PointerPressedEventArgs e)
    {
        if (e.Modifiers.HasFlag(InputModifiers.Control))
        {
            if (e.Target is IWorkflowNodeViewModel node) AgentSelection.Toggle(node);
            e.Handle.PreventDefault = true;
        }
        else if (e.Target is null && e.Modifiers.HasFlag(InputModifiers.Shift))
        {
            e.Handle.PreventDefault = true;
        }
    }

    // ── Node context menu: the shared layout presets ─────────────────────────────

    private ContextMenuStrip BuildNodeLayoutMenu(IWorkflowNodeViewModel node)
    {
        var menu = new ContextMenuStrip();

        var layout = new ToolStripMenuItem("自动布局");
        foreach (var (label, traversal) in WorkflowLayoutDemo.Traversals)
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) => ApplyNodeLayout(node, traversal);
            layout.DropDownItems.Add(item);
        }
        menu.Items.Add(layout);

        var tolerance = new ToolStripMenuItem("布局容差");
        foreach (var (label, value) in WorkflowLayoutDemo.Tolerances)
        {
            var item = new ToolStripMenuItem(label)
            {
                Checked = Math.Abs(_nodeLayoutTolerance - value) < 1e-9,
            };
            item.Click += (_, _) => _nodeLayoutTolerance = value;
            tolerance.DropDownItems.Add(item);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(tolerance);

        return menu;
    }

    private void ApplyNodeLayout(IWorkflowNodeViewModel node, WorkflowLayoutTraversal traversal)
    {
        try
        {
            WorkflowLayoutDemo.Arrange(node, traversal, _nodeLayoutTolerance);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"布局失败：{ex.Message}", "自动布局");
        }
    }
}
