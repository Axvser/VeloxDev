using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using Demo.ViewModels;
using Demo.Workflow;
using VeloxDev.WinForms.Xaml.Visuals;
using VeloxDev.WorkflowSystem;
using WorkflowBehaviors = VeloxDev.WorkflowSystem.AttachedBehaviors;
using Wf = VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

/// <summary>
/// The workflow surface host. One <see cref="VisualHost"/> paints the whole graph; the heavy machinery — pan,
/// zoom, viewport, node/slot pooling, slot layout and the rulers — belongs to
/// <see cref="WorkflowBehaviors.WorkflowSurfaceBehavior"/>.
/// What stays here is this demo's policy: session wiring, the hover highlight, the delete gesture, the Ctrl-click
/// selection veto and the Shift-wheel mapping.
/// </summary>
public sealed class WorkflowCanvas : Control
{
    private readonly VisualHost _host;
    private readonly WorkflowSurfaceView _surface;

    private WorkflowDemoSession? _session;
    private WorkflowInput? _input;

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

        DetachLinkInput();
        WorkflowBehaviors.WorkflowSurfaceBehavior.SetWorkflowTree(_host, null);
        WorkflowBehaviors.WorkflowSurfaceBehavior.Refresh(_host);
    }

    // ── Link input: hover highlight and delete ──────────────────────────────────

    private void AttachLinkInput(ITreeComponent tree)
    {
        DetachLinkInput();

        var input = WorkflowInput.For(tree);
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

    // 单层绘制从 WorkflowInput.HoveredLink 读悬停线；指针换目标时把整棵视觉树标脏就够了。
    private void UpdateLinkHighlights() => _host.Invalidate();

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
            if (e.Target is INodeComponent node) AgentSelection.Toggle(node);
            e.Handle.PreventDefault = true;
        }
        else if (e.Target is null && e.Modifiers.HasFlag(InputModifiers.Shift))
        {
            e.Handle.PreventDefault = true;
        }
    }
}
