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

    // True while the pointer is dragging this node. JS owns the wrapper position during a drag
    // (immediate, compositor-friendly), so the Anchor PropertyChanged handler must NOT reposition or
    // re-render mid-drag — that would snap the node back to a stale .NET value each frame.
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
                : $"position:absolute;left:{anchor.Horizontal.ToString("0.#")}px;top:{anchor.Vertical.ToString("0.#")}px;z-index:{anchor.Layer + 2};";
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
            // JS moves the wrapper live during a drag; for external moves (undo/redo, layout
            // commands) reposition it directly via JS — no re-render, mirroring the XAML adapters'
            // ViewManager.ApplyLayout (Canvas.SetLeft/Top only).
            if (!_isDragging)
            {
                SyncPosition();
            }
        }
        else if (e.PropertyName is nameof(IWorkflowNodeViewModel.Size))
        {
            // Size changes are rare (selector swapping content); a re-render is fine.
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
        // Snap the wrapper to the final .NET anchor so it agrees with undo/redo and serialization.
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
