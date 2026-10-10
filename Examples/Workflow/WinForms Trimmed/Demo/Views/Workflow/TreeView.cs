// VeloxDev customization: The workflow surface. The adapter's WorkflowTreeView already assembles the
// chrome, grid, floating rulers, scrolled canvas, view pool and pan engine; this file supplies only the
// palette, the two view factories, and the demo's HUD. Wire a tree through the ViewModel property.
using System.Drawing;
using System.Windows.Forms;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The workflow surface: the adapter's <see cref="WorkflowTreeView"/> with this demo's palette, its
/// generated node and link views, and the canvas-info HUD.
/// </summary>
public sealed class TreeView : WorkflowTreeView
{
    private readonly InfoOverlay _infoOverlay = new();

    private IWorkflowTreeViewModel? _tree;
    private IInputEvents? _events;
    private bool _shiftHeld;

    public TreeView()
    {
        SurfaceBackground = ParseColor("#1E1E1E");
        SurfaceBorderBrush = ParseColor("#33FFFFFF");
        SurfaceBorderThickness = 1;
        SurfaceCornerRadius = 3;

        // Realtime canvas-info HUD: floating bottom-left, above the scroll viewer.
        Controls.Add(_infoOverlay);
        _infoOverlay.BringToFront();
    }

    /// <inheritdoc />
    protected override Control CreateNodeView(IWorkflowNodeViewModel node)
    {
        var view = new NodeView { ViewModel = node };
        // The minimap reads node anchors directly; dragging a node changes the anchor without panning,
        // so repaint it as the node moves.
        view.Attachment.AnchorChanged += () =>
        {
            if (!IsDisposed && PART_MinimapOverlay is not null)
            {
                PART_MinimapOverlay.Invalidate();
            }
        };
        return view;
    }

    /// <inheritdoc />
    protected override Control CreateLinkView(IWorkflowLinkViewModel link)
        => new LinkView();

    /// <inheritdoc />
    protected override void OnBuildLinkMenu(ContextMenuStrip menu, IWorkflowLinkViewModel link)
    {
        // The menu is rebuilt on every right press. Nothing is here by default — the entries are this project's.
        menu.Items.Add("Delete", null, (_, _) => link.DeleteCommand.Execute(null));
    }

    /// <inheritdoc />
    protected override void OnTreeAttached(IWorkflowTreeViewModel? tree)
    {
        _infoOverlay.Bind(tree);
        Unwire();

        if (tree is null || tree.GetHelper() is not IInputEvents events) return;

        // 树一级的键与滚轮都订在**树的 Helper** 上 —— 与连线视图订自己那一条同一条路，
        // 适配器只负责把平台的输入翻译过来，这里决定收到之后做什么。
        _tree = tree;
        _events = events;
        events.Input.KeyDown += OnKeyDown;
        events.Input.KeyUp += OnKeyUp;
        events.Input.PointerWheelChanged += OnWheel;
    }

    private void Unwire()
    {
        if (_events is not null)
        {
            _events.Input.KeyDown -= OnKeyDown;
            _events.Input.KeyUp -= OnKeyUp;
            _events.Input.PointerWheelChanged -= OnWheel;
            _events = null;
        }

        _tree = null;
        _shiftHeld = false;
    }

    // VeloxDev customization: 撤销/重做是宿主的 —— 库只把按键路由过来，撤什么由这里写（与删除连线同一条路）。
    // ⚠ 拖动节点按 Ctrl+Z 不会撤那次移动：Move / SetAnchor / SetSize 按设计不入栈（高频几何变更不该挤爆
    // 撤销栈），撤掉的是更早的一次结构改动。要可撤销的拖动就自己提供 ActionPair 走 tree.GetHelper().Submit。
    private void OnKeyDown(object? sender, KeyDownEventArgs e)
    {
        // 任何一次按下都带着当时的修饰键状态，所以它既设 Shift，也能把失焦期间留下的陈旧状态纠正回来。
        _shiftHeld = IsShiftKey(e.Key) || e.Modifiers.HasFlag(InputModifiers.Shift);

        if (!e.Modifiers.HasFlag(InputModifiers.Control) || _tree is not { } tree) return;

        var redo = e.Modifiers.HasFlag(InputModifiers.Shift);
        if (e.Key == InputKey.Z && !redo) tree.UndoCommand.Execute(null);
        else if (e.Key == InputKey.Y || (e.Key == InputKey.Z && redo)) tree.RedoCommand.Execute(null);
        else return;

        e.Handle.PreventDefault = true;
    }

    private void OnKeyUp(object? sender, KeyUpEventArgs e)
    {
        // Shift 自己抬起时平台仍然报它按着（修饰键状态在事件之后才更新），所以这一条只能按 Key 判。
        if (IsShiftKey(e.Key)) _shiftHeld = false;
    }

    private static bool IsShiftKey(InputKey key) => key is InputKey.LeftShift or InputKey.RightShift;

    // VeloxDev customization: 画布滚轮由适配器整笔接管，所以这里是唯一能改写它去哪的机会。
    // 不按 Shift 就什么都不做 —— 适配器按平台的步长执行默认竖滚；按住了就拦下这一笔，改从轴上横着滚。
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!_shiftHeld || _tree is not { } tree) return;

        e.Handle.PreventDefault = true;
        WorkflowInput.For(tree).Scroller?.ScrollBy(e.DeltaY, 0d);
    }

    /// <inheritdoc />
    protected override void OnSurfaceRefreshed() => _infoOverlay.UpdateText();
}
