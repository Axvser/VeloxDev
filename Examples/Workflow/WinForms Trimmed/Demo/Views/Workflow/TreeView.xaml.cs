// VeloxDev customization: The workflow surface. TreeView.xaml declares the chrome, the viewport + canvas, the
// grid decorator and the minimap; this file supplies the tree binding, the link menu and the demo's HUD.
using System.ComponentModel;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace Demo.Views.Workflow;

/// <summary>
/// The workflow surface: a XAML tree of viewport, canvas, grid decorator and minimap wired through
/// <see cref="WorkflowSurfaceBehavior"/>, plus this project's node/link views and canvas HUD.
/// </summary>
public sealed partial class TreeView : VeloxDev.WinForms.Xaml.Controls.UserControl
{
    private IWorkflowTreeViewModel? _tree;
    private IInputEvents? _events;

    /// <summary>Creates the surface, runs the generated initializer and wires the link menu and HUD.</summary>
    public TreeView()
    {
        InitializeComponent();

        // The menu is rebuilt on every right press. Nothing is here by default — the entries are this project's.
        WorkflowSurfaceBehavior.SetLinkMenuBuilder(
            this, static (menu, link) => menu.Items.Add(
                "Delete", null, (_, _) => link.DeleteCommand.Execute(null)));

        // WinForms appends children to the back of the z-order; raise the floating panels over the surface.
        minimapOverlay.BringToFront();
        _infoOverlay.BringToFront();
        // The HUD reads the viewport the surface pushes on every pan/zoom; repaint it with the canvas frame.
        surfaceCanvas.Paint += (_, _) => _infoOverlay.UpdateText();
    }

    /// <summary>The workflow tree the surface renders.</summary>
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IWorkflowTreeViewModel? ViewModel
    {
        get => WorkflowSurfaceBehavior.GetWorkflowTree(this);
        set
        {
            WorkflowSurfaceBehavior.SetWorkflowTree(this, value);
            _infoOverlay.Bind(value);
            Unwire();

            // 树一级的键与滚轮都订在**树的 Helper** 上 —— 与连线视图订自己那一条同一条路，
            // 适配器只负责把平台的输入翻译过来，这里决定收到之后做什么。
            _tree = value;
            if (value?.GetHelper() is IInputEvents events)
            {
                _events = events;
                events.Input.KeyDown += OnKeyDown;
                events.Input.PointerWheelChanged += OnWheel;
            }

            WorkflowSurfaceBehavior.Refresh(this);
        }
    }

    private void Unwire()
    {
        if (_events is not null)
        {
            _events.Input.KeyDown -= OnKeyDown;
            _events.Input.PointerWheelChanged -= OnWheel;
            _events = null;
        }

        _tree = null;
    }

    // VeloxDev customization: 撤销/重做与删除都是宿主的 —— 库只把按键路由过来，做什么由这里写。
    // ⚠ 拖动节点按 Ctrl+Z 不会撤那次移动：Move / SetAnchor / SetSize 按设计不入栈（高频几何变更不该挤爆
    // 撤销栈），撤掉的是更早的一次结构改动。要可撤销的拖动就自己提供 ActionPair 走 tree.GetHelper().Submit。
    private void OnKeyDown(object? sender, KeyDownEventArgs e)
    {
        if (_tree is not { } tree) return;

        if (e.Key == InputKey.Delete)
        {
            if (WorkflowInput.For(tree).HoveredLink is { } hovered && hovered.DeleteCommand.CanExecute(null))
            {
                hovered.DeleteCommand.Execute(null);
                e.Handle.PreventDefault = true;
            }

            return;
        }

        if (!e.Modifiers.HasFlag(InputModifiers.Control)) return;

        var redo = e.Modifiers.HasFlag(InputModifiers.Shift);
        if (e.Key == InputKey.Z && !redo) tree.UndoCommand.Execute(null);
        else if (e.Key == InputKey.Y || (e.Key == InputKey.Z && redo)) tree.RedoCommand.Execute(null);
        else return;

        e.Handle.PreventDefault = true;
    }

    // VeloxDev customization: 画布滚轮由适配器整笔接管，所以这里是唯一能改写它去哪的机会。
    // 不按 Shift 就什么都不做 —— 适配器按平台的步长执行默认竖滚；按住了就拦下这一笔，改从轴上横着滚。
    // 修饰键读事件自带的那一份：它不依赖键盘焦点。
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.Modifiers.HasFlag(InputModifiers.Shift) || _tree is not { } tree) return;

        e.Handle.PreventDefault = true;
        // 竖轴正 = 远离用户，塞进横轴要取反 —— 横轴正 = 向右，而「远离用户」在画面上是向左。
        WorkflowInput.For(tree).Scroller?.ScrollBy(-e.DeltaY, 0d);
    }
}
