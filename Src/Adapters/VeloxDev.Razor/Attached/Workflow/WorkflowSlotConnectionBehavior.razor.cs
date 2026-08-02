using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Blazor analogue of the XAML adapters' <c>WorkflowSlotConnectionBehavior</c> attached property.
/// Wraps a slot's content and translates connection gestures into the core
/// <c>SendConnection</c>/<c>SetPointer</c>/<c>ReceiveConnection</c>/<c>ResetVirtualLink</c> commands.
/// </summary>
public partial class WorkflowSlotConnectionBehavior : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    /// <summary>Gets or sets the slot that initiates and receives connections.</summary>
    [Parameter]
    public IWorkflowSlotViewModel? Slot { get; set; }

    /// <summary>Gets or sets the workflow tree that owns the connection gesture.</summary>
    [Parameter]
    public IWorkflowTreeViewModel? Tree { get; set; }

    /// <summary>Gets or sets whether slot connection dragging is enabled.</summary>
    [Parameter]
    public bool IsEnabled { get; set; } = true;

    /// <summary>Gets or sets the wrapper element style.</summary>
    [Parameter]
    public string? Style { get; set; }

    /// <summary>Gets or sets the slot content.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private ElementReference _element;
    private IJSObjectReference? _module;
    private DotNetObjectReference<WorkflowSlotConnectionBehavior>? _dotNetRef;
    private IJSObjectReference? _handle;

    private string? SlotId => Slot is null ? null : WorkflowRuntimeIds.Get(Slot);

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (firstRender && IsEnabled)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/VeloxDev.Razor/veloxdev.workflow.js");
            _dotNetRef = DotNetObjectReference.Create(this);
            _handle = await _module.InvokeAsync<IJSObjectReference>("initSlotConnection", _element, _dotNetRef);
        }
    }

    [JSInvokable]
    public void OnSlotConnectionStart()
    {
        if (Slot is null || Tree is null)
        {
            return;
        }

        if (Slot.SendConnectionCommand.CanExecute(null))
        {
            Slot.SendConnectionCommand.Execute(null);
        }
    }

    [JSInvokable]
    public void OnSlotConnectionMove(double worldX, double worldY)
    {
        if (Tree is null)
        {
            return;
        }

        var anchor = new Anchor(worldX, worldY, 0);
        if (Tree.SetPointerCommand.CanExecute(anchor))
        {
            Tree.SetPointerCommand.Execute(anchor);
        }
    }

    [JSInvokable]
    public void OnSlotConnectionEnd(string? targetSlotId)
    {
        if (Tree is null || Slot is null)
        {
            return;
        }

        if (WorkflowRuntimeIds.TryFind<IWorkflowSlotViewModel>(targetSlotId, out var target)
            && target is not null
            && !ReferenceEquals(target, Slot))
        {
            if (target.ReceiveConnectionCommand.CanExecute(null))
            {
                target.ReceiveConnectionCommand.Execute(null);
                return;
            }
        }

        if (Tree.ResetVirtualLinkCommand.CanExecute(null))
        {
            Tree.ResetVirtualLinkCommand.Execute(null);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
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
