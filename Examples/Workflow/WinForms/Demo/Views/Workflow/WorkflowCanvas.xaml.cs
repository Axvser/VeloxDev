using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Demo.ViewModels;
using Demo.Workflow;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WinForms.Xaml.Visuals.Controls;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;
using Wf = VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// The workflow surface host. One <see cref="VisualHost"/> paints the whole graph; the heavy machinery — pan,
/// zoom, viewport, node/slot pooling, slot layout, the rulers and the hover highlight — belongs to
/// <see cref="WorkflowBehaviors.WorkflowSurfaceBehavior"/>.
/// What stays here is this demo's policy: session wiring, the node layout menu, the delete gesture, the Ctrl-click
/// selection veto and the Shift-wheel mapping.
/// </summary>
public sealed class WorkflowCanvas : Control
{
    private readonly VisualHost _host;
    private readonly WorkflowSurfaceView _surface;
    private readonly List<(VisualMenuItem Item, double Tolerance)> _toleranceItems = [];

    private WorkflowDemoSession? _session;
    private WorkflowInput? _input;
    private VisualContextMenu? _nodeMenu;
    private INodeComponent? _nodeMenuTarget;
    private double _nodeLayoutTolerance = 8d;

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

    /// <summary>The minimap part, for host-level show/hide policy.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public MinimapOverlay MinimapOverlay => _surface.MinimapOverlay;

    /// <summary>The canvas-info HUD, exposed so the host can copy its text.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public WorkflowBehaviors.WorkflowInfoOverlay InfoOverlay => _surface.InfoOverlay;

    /// <summary>Raised with a one-line message for the host's status area after a surface gesture.</summary>
    public event EventHandler<string>? StatusMessage;

    /// <summary>Creates the surface and its single visual host.</summary>
    public WorkflowCanvas()
    {
        _surface = new WorkflowSurfaceView();
        _host = new VisualHost { Dock = DockStyle.Fill, Root = _surface };

        Controls.Add(_host);

        WorkflowBehaviors.WorkflowSurfaceBehavior.SetCanvasName(_host, "PART_Canvas");
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetScrollViewerName(_host, "PART_ScrollViewer");
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetGridDecoratorName(_host, "PART_GridDecorator");
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetMinimapOverlayName(_host, "PART_MinimapOverlay");
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetPointerPressSourceName(_host, "PART_SurfaceBorder");
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetLinkMenuKey(_host, "LinkContextMenu");
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetZoomEnabled(_host, true);
        // The surface view declares WorkflowTemplateSelector; WorkflowSurfaceBehavior resolves that key.
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetVisualLinkColor(_host, Color.FromArgb(0xCC, 0x6E, 0x8B, 0xFF));
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetVisualLinkThickness(_host, 2f);
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetVisualLinkHoverColor(_host, Color.White);
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetVisualLinkHoverThickness(_host, 3.5f);
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetVisualLinkVirtualColor(_host, Color.FromArgb(0xCC, 0x6E, 0x8B, 0xFF));
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetVisualLinkGlow(_host, true);
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(_host, true);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            WorkflowBehaviors.WorkflowSurfaceBehavior.SetIsEnabled(_host, false);

            if (_session?.Tree.ResetVirtualLinkCommand.CanExecute(null) == true)
            {
                _session.Tree.ResetVirtualLinkCommand.Execute(null);
            }

            DetachSession(_session);

            _nodeMenu?.Close();
            _nodeMenu?.Dispose();
            _nodeMenu = null;
        }

        base.Dispose(disposing);
    }

    // ── Session lifecycle ───────────────────────────────────────────────────────

    private void AttachSession(WorkflowDemoSession? session)
    {
        if (session is null) return;

        WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(_host, session.Tree);
        AttachLinkInput(session.Tree);
        VetoFrameworkGestures(session.Tree);
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(_host);
    }

    private void DetachSession(WorkflowDemoSession? session)
    {
        if (session is null) return;

        _nodeMenu?.Close();
        _nodeMenuTarget = null;
        DetachLinkInput();
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(_host, null);
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(_host);
    }

    // ── Link input: delete, node menu and Shift-wheel ───────────────────────────

    private void AttachLinkInput(ITreeComponent tree)
    {
        DetachLinkInput();

        var input = WorkflowInput.For(tree);
        _input = input;

        if (tree.GetHelper() is Wf.IInputEvents events)
        {
            events.Input.PointerPressed += OnNodeMenuPointerPressed;
            events.Input.KeyDown += OnInputKeyDown;
            events.Input.PointerWheelChanged += OnInputPointerWheel;
        }
    }

    private void DetachLinkInput()
    {
        if (_input is not { } input) return;

        if (input.Tree.GetHelper() is Wf.IInputEvents events)
        {
            events.Input.PointerPressed -= OnNodeMenuPointerPressed;
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

    // ── Node right-click menu: the shared layout presets ────────────────────────

    // 右键节点由这里弹菜单。订阅的是 Core 的按下事件：命中者不是节点就放行 —— 连线右键仍归适配器的
    // LinkMenuKey，节点的左键拖动与端口连线也不经过这一支（它们只在左键上应答）。
    private void OnNodeMenuPointerPressed(object? sender, Wf.PointerPressedEventArgs e)
    {
        if (e.Button != Wf.MouseButton.Right
            || e.Target is not INodeComponent node
            || e.Handle.PreventDefault)
        {
            return;
        }

        if (ShowNodeLayoutMenu(node, e.Position))
        {
            e.Handle.PreventDefault = true;
        }
    }

    private bool ShowNodeLayoutMenu(INodeComponent node, Wf.Anchor position)
    {
        var surfacePoint = WorkflowBehaviors.WorkflowSurfaceBehavior.WorldToSurface(
            _host, new PointF((float)position.Horizontal, (float)position.Vertical));

        // 脚本框之类的文字输入保留自己的右键语义，与其余平台一致。
        var hit = _host.FindElementAt(surfacePoint);
        if (IsInsideTextInput(hit))
        {
            return false;
        }

        _nodeMenuTarget = node;
        var menu = EnsureNodeMenu();
        RefreshToleranceChecks();
        menu.Refresh();
        menu.OpenAt(ResolveNodeView(hit, node) ?? (VisualElement)_surface, surfacePoint);
        return true;
    }

    // 菜单以节点卡为锚（与其余平台一致）；指针若落在已挂的弹层上，就退回表面原点。
    private static VisualElement? ResolveNodeView(VisualElement? hit, INodeComponent node)
    {
        for (var current = hit; current is not null; current = current.ParentElement)
        {
            if (ReferenceEquals(current.DataContext, node))
            {
                return current;
            }
        }

        return null;
    }

    private static bool IsInsideTextInput(VisualElement? hit)
    {
        for (var current = hit; current is not null; current = current.ParentElement)
        {
            if (current is VeloxDev.WinForms.Xaml.Visuals.Controls.TextBox
                or VeloxDev.WinForms.Xaml.Visuals.Controls.RichTextBox)
            {
                return true;
            }

            if (current is WorkflowSurfaceView)
            {
                break;
            }
        }

        return false;
    }

    // 菜单只建一次，点的是「当前右键的那个节点」；重建一次的只有容差勾选状态。
    private VisualContextMenu EnsureNodeMenu()
    {
        if (_nodeMenu is not null)
        {
            return _nodeMenu;
        }

        var menu = new VisualContextMenu();

        var layout = new VisualMenuItem { Header = "自动布局" };
        foreach (var (label, traversal) in WorkflowLayoutDemo.Traversals)
        {
            var item = new VisualMenuItem { Header = label };
            item.Click += (_, _) => ApplyNodeLayout(traversal);
            layout.Items.Add(item);
        }

        menu.Items.Add(layout);
        menu.Items.Add(new VisualMenuItem { IsSeparator = true });

        var tolerance = new VisualMenuItem { Header = "布局容差" };
        foreach (var (label, value) in WorkflowLayoutDemo.Tolerances)
        {
            var item = new VisualMenuItem { Header = label };
            item.Click += (_, _) =>
            {
                _nodeLayoutTolerance = value;
                RefreshToleranceChecks();
                ReportStatus($"布局容差 = {value}");
            };
            _toleranceItems.Add((item, value));
            tolerance.Items.Add(item);
        }

        menu.Items.Add(tolerance);
        _nodeMenu = menu;
        RefreshToleranceChecks();
        return menu;
    }

    private void RefreshToleranceChecks()
    {
        foreach (var (item, value) in _toleranceItems)
        {
            item.IsChecked = Math.Abs(_nodeLayoutTolerance - value) < 1e-9d;
        }
    }

    private void ApplyNodeLayout(WorkflowLayoutTraversal traversal)
    {
        if (_nodeMenuTarget is not { } node) return;

        try
        {
            var result = WorkflowLayoutDemo.Arrange(node, traversal, _nodeLayoutTolerance);
            ReportStatus(WorkflowLayoutDemo.Describe(result, $"从 {node.GetType().Name}"));
            WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(_host);
        }
        catch (Exception ex)
        {
            ReportStatus($"布局失败：{ex.Message}");
        }
    }

    private void ReportStatus(string message) => StatusMessage?.Invoke(this, message);

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
            if (e.Target is INodeComponent node) AgentSelection.Toggle(node);
            e.Handle.PreventDefault = true;
        }
        else if (e.Target is null && e.Modifiers.HasFlag(InputModifiers.Shift))
        {
            e.Handle.PreventDefault = true;
        }
    }
}
