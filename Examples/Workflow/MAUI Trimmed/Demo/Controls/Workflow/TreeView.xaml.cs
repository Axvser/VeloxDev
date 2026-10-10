// VeloxDev customization: Set BindingContext to your IWorkflowTreeViewModel before the control is loaded.
using VeloxDev.WorkflowSystem;

namespace Demo.Controls;

public partial class TreeView : ContentView
{
    public TreeView()
    {
        InitializeComponent();

        // Keep the canvas-info HUD current on every scroll / viewport change (it reads helper.Viewport,
        // which the surface behavior refreshes; the model events cover scale / visible counts).
        PART_ScrollViewer.Scrolled += (_, _) => InfoOverlay.Update();
        PART_ScrollViewer.SizeChanged += (_, _) => InfoOverlay.Update();

        // The tree is assigned to this control by the page; propagate it explicitly so the HUD's
        // BindingContextChanged fires even if inheritance doesn't reach the nested overlay.
        // SyncLinkInput 必须挂在这里，不能只在构造里调一次：构造函数跑的时候 BindingContext 还是 null
        //（本文件的约定就是「由页面在加载前赋给它」），那一次订阅会静默落空 —— 悬停照常亮（那是 overlay
        // 自己的事），但 Delete 永远到不了宿主。
        BindingContextChanged += (_, _) =>
        {
            InfoOverlay.BindingContext = BindingContext;
            SyncLinkInput();
        };
    }

    // VeloxDev customization: 悬停高亮是这本 demo 的。overlay 默认什么都不画，订阅树的输入事件、
    // 把「现在轮到哪条线」交给它，它才照 #FFFFFFFF 画那一条 —— 换色/换画法都在这里改。
    // 树一级的键与滚轮也订在**树的 Helper** 上 —— 与连线视图订自己那一条同一条路，适配器只负责把
    // 平台的输入翻译过来，这里决定收到之后做什么。
    private IInputEvents? _linkInput;
    private IWorkflowTreeViewModel? _tree;
    private bool _shiftHeld;

    private void SyncLinkInput()
    {
        if (_linkInput is not null)
        {
            _linkInput.Input.KeyDown -= OnKeyDown;
            _linkInput.Input.KeyUp -= OnKeyUp;
            _linkInput.Input.PointerWheelChanged -= OnWheel;
            _linkInput = null;
        }

        _tree = BindingContext as IWorkflowTreeViewModel;
        _shiftHeld = false;

        if (_tree is not null && _tree.GetHelper() is IInputEvents events)
        {
            _linkInput = events;

            // VeloxDev customization: 删除与撤销/重做都是宿主的 —— 路由把这次按键交过来（target 就是
            // 指针下的那条线，空白画布时链的尽头是树）。
            events.Input.KeyDown += OnKeyDown;
            events.Input.KeyUp += OnKeyUp;
            events.Input.PointerWheelChanged += OnWheel;
        }
    }

    // VeloxDev customization: 撤销/重做是宿主的 —— 库只把按键路由过来，撤什么由这里写（与删除连线同一条路）。
    // ⚠ 拖动节点按 Ctrl+Z **不会**撤那次移动：Move / SetAnchor / SetSize 按设计不入栈（高频几何变更不该挤爆
    // 撤销栈），撤掉的是更早的一次结构改动。要可撤销的拖动就自己提供 ActionPair 走 tree.GetHelper().Submit。
    private void OnKeyDown(object? sender, KeyDownEventArgs e)
    {
        // 任何一次按下都带着当时的修饰键状态，所以它既设 Shift，也能把失焦期间留下的陈旧状态纠正回来。
        _shiftHeld = IsShiftKey(e.Key) || e.Modifiers.HasFlag(InputModifiers.Shift);

        if (e.Handle.PreventDefault) return;

        if (e.Key == InputKey.Delete)
        {
            if (e.Target is IWorkflowLinkViewModel link && link.DeleteCommand.CanExecute(null))
            {
                link.DeleteCommand.Execute(null);
            }

            return;
        }

        if (!e.Modifiers.HasFlag(InputModifiers.Control) || _tree is not { } tree) return;

        var redo = e.Modifiers.HasFlag(InputModifiers.Shift);
        if (e.Key == InputKey.Z && !redo) tree.UndoCommand.Execute(null);
        else if (e.Key == InputKey.Y || (e.Key == InputKey.Z && redo)) tree.RedoCommand.Execute(null);
        else return;

        e.Handle.PreventDefault = true;
    }

    private void OnKeyUp(object? sender, KeyUpEventArgs e)
    {
        // Shift 自己抬起时平台**仍然**报它按着（修饰键状态在事件之后才更新），所以这一条只能按 Key 判。
        if (IsShiftKey(e.Key)) _shiftHeld = false;
    }

    private static bool IsShiftKey(InputKey key) => key is InputKey.LeftShift or InputKey.RightShift;

    // VeloxDev customization: 画布滚轮由适配器整笔接管，所以这里是**唯一**能改写它去哪的机会。
    // 不按 Shift 就什么都不做 —— 适配器按平台的步长执行默认竖滚；按住了就拦下这一笔，改从轴上横着滚。
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!_shiftHeld || _tree is not { } tree) return;

        e.Handle.PreventDefault = true;
        WorkflowInput.For(tree).Scroller?.ScrollBy(e.DeltaY, 0d);
    }
}
