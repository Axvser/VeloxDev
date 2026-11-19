using System;
using System.ComponentModel;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Blazor analogue of the XAML adapters' <c>WorkflowNodeDragBehavior</c> attached property.
/// Renders the wrapped node content as an absolutely-positioned element whose left/top/z-index
/// track <see cref="IWorkflowNodeViewModel.Anchor"/>, and translates pointer drags into
/// <see cref="IWorkflowNodeViewModel.MoveCommand"/> executions (world deltas), mirroring the
/// WinForms implementation.
/// </summary>
public partial class WorkflowNodeDragBehavior : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    /// <summary>Gets or sets the node to render and move while dragging.</summary>
    [Parameter]
    public IWorkflowNodeViewModel? Node { get; set; }

    /// <summary>Gets or sets whether node dragging is enabled.</summary>
    [Parameter]
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets extra styles appended to the positioned wrapper (e.g. width/height).
    /// Position (left/top/z-index) is computed from <see cref="Node"/> and updates live on drag.
    /// </summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>Gets or sets the node content.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private ElementReference _element;
    private IJSObjectReference? _module;
    private DotNetObjectReference<WorkflowNodeDragBehavior>? _dotNetRef;
    private IJSObjectReference? _handle;
    private IWorkflowNodeViewModel? _subscribedNode;

    // 指针拖拽本节点期间为真。拖拽中包装元素位置归 JS（即时、利于合成器），所以 Anchor 的 PropertyChanged 处理器在拖拽中不能重新定位或重渲染 —— 那会每帧把节点弹回陈旧的 .NET 值。
    private bool _isDragging;

    /// <summary>
    /// Stable per-node id rendered as <c>data-veloxdev-node-id</c> so the surface's JS
    /// <c>applyZoomSurface</c> can map a collapsed-geometry array to this exact wrapper
    /// (reference-stable across re-renders, like the slot ids).
    /// </summary>
    private string? NodeId => Node is null ? null : WorkflowRuntimeIds.Get(Node);

    private string WrapperStyle
    {
        get
        {
            var anchor = Node?.Anchor;
            var position = anchor is null
                ? "position:absolute;left:0px;top:0px;"
                : $"position:absolute;left:{anchor.Horizontal.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}px;top:{anchor.Vertical.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}px;z-index:{anchor.Layer + 2};";
            return position + Style;
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        if (!ReferenceEquals(_subscribedNode, Node))
        {
            if (_subscribedNode is INotifyPropertyChanged oldNpc)
            {
                oldNpc.PropertyChanged -= OnNodePropertyChanged;
            }

            _subscribedNode = Node;
            if (Node is INotifyPropertyChanged npc)
            {
                npc.PropertyChanged += OnNodePropertyChanged;
            }
        }
    }

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IWorkflowNodeViewModel.Anchor))
        {
            // 缩放时每个节点的折叠由表面的 applyZoomSurface 同步盖章（与滚动/平移原子）；这里让位，避免逐节点 setNodePosition 抢先到浏览器、画出错位的包装元素。
            if (WorkflowGeometryScope.IsZooming)
            {
                return;
            }

            // 拖拽中包装元素由 JS 实时移动；外部移动（撤销/重做、布局命令）也直接经 JS 重定位 —— 不重渲染，与 XAML 几家 ViewManager.ApplyLayout（只 Canvas.SetLeft/Top）一致。
            if (!_isDragging)
            {
                SyncPosition();
            }
        }
        else if (e.PropertyName is nameof(IWorkflowNodeViewModel.Size))
        {
            // 缩放中 JS 已重新盖章折叠后的卡片变换；这里再重渲染会用舍入不同的字节跟它打架。
            if (WorkflowGeometryScope.IsZooming)
            {
                return;
            }

            // 尺寸变化很少（选择器换内容），重渲染即可。
            InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Repositions the wrapper element to the node's current anchor via JS, without a Blazor
    /// re-render. Used for external anchor changes; during an active drag the JS already owns the
    /// position and this is skipped.
    /// </summary>
    private void SyncPosition()
    {
        if (_module is not null && Node is not null && !string.IsNullOrEmpty(_element.Id))
        {
            _ = _module.InvokeVoidAsync("setNodePosition",
                _element, Node.Anchor.Horizontal, Node.Anchor.Vertical);
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (firstRender && IsEnabled)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/VeloxDev.Razor/veloxdev.workflow.js");
            _dotNetRef = DotNetObjectReference.Create(this);
            _handle = await _module.InvokeAsync<IJSObjectReference>("initNodeDrag", _element, _dotNetRef);
        }
    }

    /// <summary>
    /// Answers the JavaScript's press before it starts a drag: routes the press on the node's own relay and
    /// hands back whether a subscriber refused it.
    /// </summary>
    /// <param name="localX">Canvas-local x of the press.</param>
    /// <param name="localY">Canvas-local y of the press.</param>
    /// <param name="button">The browser's <c>MouseEvent.button</c> number.</param>
    /// <param name="modifiers">The Core modifier bitmask.</param>
    /// <param name="targetId">
    /// The id the shared JavaScript resolved from the DOM. Unused here: this component already knows the
    /// component it is (the parameter is only there so both sides of the interop agree on the signature).
    /// </param>
    /// <returns><see langword="true"/> when a subscriber set <see cref="WorkflowEventHandle.PreventDefault"/>.</returns>
    [JSInvokable]
    public bool RequestPressVerdict(double localX, double localY, int button, int modifiers, string? targetId)
    {
        // 拖拽由这一笔按下触发；订阅者在节点自己的 InputRelay 上置 PreventDefault 就是「这一次别拖」。
        // 节点必须已在树上，路由才有从（Node.Parent 就是它所属的树）。
        if (Node is not { } node || node.Parent is not { } tree)
        {
            return false;
        }

        return WorkflowSurfaceBehavior.RouteComponentPress(tree, node, localX, localY, button, modifiers);
    }

    [JSInvokable]
    public void OnNodeDrag(double dx, double dy)
    {
        if (Node is null || (dx == 0 && dy == 0))
        {
            return;
        }

        _isDragging = true;
        var offset = new Offset(dx, dy);
        if (Node.MoveCommand.CanExecute(offset))
        {
            Node.MoveCommand.Execute(offset);
        }
    }

    [JSInvokable]
    public void OnNodeDragEnd()
    {
        _isDragging = false;
        // 把包装元素吸附到最终的 .NET 锚点，与撤销/重做和序列化保持一致。
        SyncPosition();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_subscribedNode is INotifyPropertyChanged npc)
        {
            npc.PropertyChanged -= OnNodePropertyChanged;
        }

        if (_handle is not null)
        {
            try
            {
                await _handle.InvokeVoidAsync("dispose");
            }
            catch
            {
            }

            try
            {
                await _handle.DisposeAsync();
            }
            catch
            {
            }
        }

        _dotNetRef?.Dispose();
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch
            {
            }
        }
    }
}
