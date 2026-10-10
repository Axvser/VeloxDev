using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VeloxDev.WorkflowSystem;

namespace Demo.Views.Workflow;

public sealed partial class TreeView : UserControl
{
    private IWorkflowTreeViewModel? _tree;
    private IInputEvents? _events;
    private bool _shiftHeld;

    public TreeView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        Unwire();

        if (args.NewValue is not IWorkflowTreeViewModel tree) return;
        if (tree.GetHelper() is not IInputEvents events) return;

        // VeloxDev customization: 树一级的键与滚轮都订在**树的 Helper** 上 —— 与连线视图订自己那一条同一条路，
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
    // ⚠ 拖动节点按 Ctrl+Z **不会**撤那次移动：Move / SetAnchor / SetSize 按设计不入栈（高频几何变更不该挤爆
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
