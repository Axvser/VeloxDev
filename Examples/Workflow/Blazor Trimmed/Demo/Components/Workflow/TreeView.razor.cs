using Microsoft.AspNetCore.Components;
using VeloxDev.WorkflowSystem;

namespace Demo.Components.Workflow;

/// <summary>
/// A Razor/Blazor workflow tree surface composing the surface behavior, grid decorator,
/// minimap, and a pooled node/link view layer. Set <see cref="Tree"/> to an
/// <see cref="IWorkflowTreeViewModel"/> to render; re-rendering, slot enumeration and the
/// default palette are supplied by the surface behavior. Override <see cref="NodeTemplate"/>
/// to replace the generated node card.
/// </summary>
public partial class TreeView : ComponentBase, IDisposable
{
    /// <summary>Gets or sets the workflow tree rendered by this surface.</summary>
    [Parameter]
    public IWorkflowTreeViewModel? Tree { get; set; }

    /// <summary>Gets or sets the scroll container element id.</summary>
    [Parameter]
    public string ScrollViewerId { get; set; } = "veloxdev-wf-scroll";

    /// <summary>Gets or sets the canvas element id.</summary>
    [Parameter]
    public string CanvasId { get; set; } = "veloxdev-wf-canvas";

    /// <summary>Gets or sets an optional per-node template (overrides the generated <c>NodeView</c>).</summary>
    [Parameter]
    public RenderFragment<IWorkflowNodeViewModel>? NodeTemplate { get; set; }

    /// <summary>Gets or sets the minor grid spacing in pixels.</summary>
    [Parameter]
    public double GridSpacing { get; set; } = 40;

    // VeloxDev customization: 树一级的键与滚轮都订在**树的 Helper** 上 —— 与连线视图订自己那一条同一条路，
    // 适配器只负责把平台的输入翻译过来，这里决定收到之后做什么。
    private IWorkflowTreeViewModel? _subscribedTree;
    private IInputEvents? _events;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        // 换树才重接：按模型实例比对，重复的参数设置不再动订阅。
        if (!ReferenceEquals(_subscribedTree, Tree))
        {
            SyncTreeInput();
        }
    }

    private void SyncTreeInput()
    {
        Unwire();

        _subscribedTree = Tree;

        if (Tree is not null && Tree.GetHelper() is IInputEvents events)
        {
            _events = events;
            events.Input.KeyDown += OnKeyDown;
            events.Input.PointerWheelChanged += OnWheel;
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
    }

    // VeloxDev customization: 撤销/重做是宿主的 —— 库只把按键路由过来，撤什么由这里写（与删除连线同一条路）。
    // ⚠ 拖动节点按 Ctrl+Z **不会**撤那次移动：Move / SetAnchor / SetSize 按设计不入栈（高频几何变更不该挤爆
    // 撤销栈），撤掉的是更早的一次结构改动。要可撤销的拖动就自己提供 ActionPair 走 tree.GetHelper().Submit。
    private void OnKeyDown(object? sender, KeyDownEventArgs e)
    {
        if (!e.Modifiers.HasFlag(InputModifiers.Control) || Tree is not { } tree) return;

        var redo = e.Modifiers.HasFlag(InputModifiers.Shift);
        if (e.Key == InputKey.Z && !redo) tree.UndoCommand.Execute(null);
        else if (e.Key == InputKey.Y || (e.Key == InputKey.Z && redo)) tree.RedoCommand.Execute(null);
        else return;

        e.Handle.PreventDefault = true;
    }

    // VeloxDev customization: 画布滚轮由适配器整笔接管，所以这里是**唯一**能改写它去哪的机会。
    // 不按 Shift 就什么都不做 —— 适配器按浏览器的步长执行默认竖滚；按住了就拦下这一笔，改从轴上横着滚。
    // 修饰键读事件自带的那一份：它不依赖键盘焦点。用 KeyDown/KeyUp 跟踪的话，表面没焦点时 Shift 永远不来。
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (!e.Modifiers.HasFlag(InputModifiers.Shift) || Tree is not { } tree) return;

        e.Handle.PreventDefault = true;
        // 竖轴正 = 远离用户，塞进横轴要取反 —— 横轴正 = 向右，而「远离用户」在画面上是向左。
        WorkflowInput.For(tree).Scroller?.ScrollBy(-e.DeltaY, 0d);
    }

    /// <inheritdoc />
    public void Dispose() => Unwire();
}
