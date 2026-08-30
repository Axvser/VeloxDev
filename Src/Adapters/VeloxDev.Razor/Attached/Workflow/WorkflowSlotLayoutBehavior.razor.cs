using System;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Blazor analogue of the XAML adapters' <c>WorkflowSlotLayoutBehavior</c> attached property.
/// Measures the on-screen centers of the wrapped slot elements (which expose <c>data-slot-id</c>)
/// and writes them back to each slot's <see cref="IWorkflowSlotViewModel.Anchor"/> in canvas
/// (world) coordinates.
/// </summary>
public partial class WorkflowSlotLayoutBehavior : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    /// <summary>Gets or sets the node whose slots should be synchronized.</summary>
    [Parameter]
    public IWorkflowNodeViewModel? Node { get; set; }

    /// <summary>Gets or sets whether slot layout synchronization is enabled.</summary>
    [Parameter]
    public bool IsEnabled { get; set; }

    /// <summary>Gets or sets the coordinate host id used for world-coordinate math (reserved).</summary>
    [Parameter]
    public string? CoordinateHostId { get; set; }

    /// <summary>Gets or sets the node content (including slot elements with <c>data-slot-id</c>).</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    private ElementReference _element;
    private IJSObjectReference? _module;
    private DotNetObjectReference<WorkflowSlotLayoutBehavior>? _dotNetRef;
    private IJSObjectReference? _handle;

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        await base.OnAfterRenderAsync(firstRender);

        if (firstRender && IsEnabled)
        {
            _module = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/VeloxDev.Razor/veloxdev.workflow.js");
            _dotNetRef = DotNetObjectReference.Create(this);
            _handle = await _module.InvokeAsync<IJSObjectReference>("initSlotLayout", _element, _dotNetRef);
        }
    }

    [JSInvokable]
    public void OnSlotLayoutBatch(string[][] batch)
    {
        if (Node is null)
        {
            return;
        }

        var anyChanged = false;
        foreach (var entry in batch)
        {
            if (entry.Length < 3)
            {
                continue;
            }

            var id = entry[0];
            if (!double.TryParse(entry[1], out var x) || !double.TryParse(entry[2], out var y))
            {
                continue;
            }

            if (!WorkflowRuntimeIds.TryFind<IWorkflowSlotViewModel>(id, out var slot) || slot is null)
            {
                continue;
            }

            slot.Anchor = new Anchor(x, y, slot.Anchor.Layer);
            anyChanged = true;
        }

        // Notify the node that its slot layout changed. Consumers render links from slot anchors,
        // so this makes them redraw immediately during a live drag instead of waiting for the
        // node's own Anchor to change again.
        if (anyChanged)
        {
            Node.OnPropertyChanged(nameof(IWorkflowNodeViewModel.Anchor));
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
