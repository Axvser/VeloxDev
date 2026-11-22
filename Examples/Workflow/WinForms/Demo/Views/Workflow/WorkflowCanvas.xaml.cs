using Demo.ViewModels;
using Demo.Workflow;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.TransitionSystem;
using VeloxDev.WinForms.Xaml.Visuals.Controls;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.VisualHosting;
using Wf = VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// The workflow surface host. The reusable <see cref="VisualWorkflowSurface"/> owns pan, zoom, the visual node cards,
/// the single link layer, slot anchors and the standard input route. What stays here is this demo's policy: session
/// wiring, the link-flow clock, the delete/undo/redo gestures, the Ctrl-click selection veto and the node menu.
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

    /// <summary>Whether the visual minimap is shown.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool IsMinimapVisible
    {
        get => workflowHost.IsMinimapVisible;
        set => workflowHost.IsMinimapVisible = value;
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

    /// <summary>Creates the surface, wires the demo's menus and binds the HUD.</summary>
    public WorkflowCanvas()
    {
        InitializeComponent();

        SetStyle(ControlStyles.Selectable, true);
        TabStop = true;

        workflowHost.BackColor = CardTheme.Ground;
        workflowHost.GridDecorator.Background = CardTheme.Ground;
        workflowHost.LinkLayer.LineColor = Color.FromArgb(0xCC, 0x6E, 0x8B, 0xFF);
        workflowHost.LinkLayer.HoverLineColor = Color.White;
        workflowHost.LinkLayer.LineWidth = 2f;
        workflowHost.LinkLayer.HoverLineWidth = 3.5f;
        workflowHost.LinkLayer.Glow = true;
        workflowHost.LinkLayer.FlowColor = Color.FromArgb(0xE6, 0xE7, 0xE7, 0xE7);
        workflowHost.Minimap.NodeBrush = Color.FromArgb(0xDC, 0x6E, 0x8B, 0xFF);
        workflowHost.NodeViewFactory = static _ => new DemoWorkflowNodeView();

        workflowHost.LinkMenuBuilder = static (menu, link) => menu.AddItem("Delete", () =>
        {
            if (link.DeleteCommand.CanExecute(null))
            {
                link.DeleteCommand.Execute(null);
            }
        });
        workflowHost.NodeMenuBuilder = BuildNodeLayoutMenu;

        // WinForms 把子控件追加到 z 序末尾（最下）；把 HUD 提上来。
        _infoOverlay.BringToFront();

        // 视口数是 surface 在平移/缩放后推的，HUD 随画布每帧重算一次即可。
        workflowHost.Paint += (_, _) => _infoOverlay.UpdateText();
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
            if (_session?.Tree.ResetVirtualLinkCommand.CanExecute(null) == true)
            {
                _session.Tree.ResetVirtualLinkCommand.Execute(null);
            }

            DetachSession(_session);
            workflowHost.NodeMenuBuilder = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>Re-reads the tree and the current viewport, for hosts that toggled the surface's visibility.</summary>
    public new void Refresh() => workflowHost.Refresh();

    // ── Session lifecycle ───────────────────────────────────────────────────────

    private void AttachSession(WorkflowDemoSession? session)
    {
        if (session is null) return;

        workflowHost.Tree = session.Tree;
        AttachLinkInput(session.Tree);
        VetoFrameworkGestures(session.Tree);
        _infoOverlay.Bind(session.Tree);
        workflowHost.Refresh();
        StartLinkFlow();
    }

    private void DetachSession(WorkflowDemoSession? session)
    {
        if (session is null) return;

        StopLinkFlow();
        DetachLinkInput();
        workflowHost.Tree = null;
        _infoOverlay.Bind(null);
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

    private void PushFlow() => workflowHost.LinkLayer.SetFlow(_bandHead, _bandIntensity);

    // ── Link input: delete, undo/redo and the shifted wheel ─────────────────────

    private void AttachLinkInput(ITreeComponent tree)
    {
        DetachLinkInput();

        var input = WorkflowInput.For(tree);
        input.HitRadius = LinkHitRadius;
        _input = input;

        if (tree.GetHelper() is Wf.IInputEvents events)
        {
            events.Input.KeyDown += OnInputKeyDown;
            events.Input.PointerWheelChanged += OnInputPointerWheel;
        }
    }

    private void DetachLinkInput()
    {
        if (_input is not { } input) return;

        if (input.Tree.GetHelper() is Wf.IInputEvents events)
        {
            events.Input.KeyDown -= OnInputKeyDown;
            events.Input.PointerWheelChanged -= OnInputPointerWheel;
        }

        // 解绑前清掉指针目标：路由跟着树活着，不清会留一条亮线。
        input.Route(new Wf.PointerExitedEventArgs(
            new Anchor(), Wf.InputModifiers.None, this, null, new WorkflowEventHandle()));
        _input = null;
    }

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
    /// The presses this host has claimed: Ctrl-click toggles agent selection, Shift-click on the empty canvas is
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
            if (e.Target is INodeComponent node) AgentSelection.Toggle(node);
            e.Handle.PreventDefault = true;
        }
        else if (e.Target is null && e.Modifiers.HasFlag(InputModifiers.Shift))
        {
            e.Handle.PreventDefault = true;
        }
    }

    // ── Node context menu: the shared layout presets ─────────────────────────────

    private void BuildNodeLayoutMenu(VisualWorkflowMenu menu, INodeComponent node)
    {
        var layout = new VisualMenuItem { Header = "自动布局" };
        foreach (var (label, traversal) in WorkflowLayoutDemo.Traversals)
        {
            var item = new VisualMenuItem { Header = label };
            item.Command = () => ApplyNodeLayout(node, traversal);
            layout.Items.Add(item);
        }
        menu.Items.Add(layout);

        var tolerance = new VisualMenuItem { Header = "布局容差" };
        foreach (var (label, value) in WorkflowLayoutDemo.Tolerances)
        {
            var item = new VisualMenuItem
            {
                Header = label,
                IsChecked = Math.Abs(_nodeLayoutTolerance - value) < 1e-9,
            };
            item.Command = () => _nodeLayoutTolerance = value;
            tolerance.Items.Add(item);
        }

        menu.AddSeparator();
        menu.Items.Add(tolerance);
    }

    private void ApplyNodeLayout(INodeComponent node, WorkflowLayoutTraversal traversal)
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
