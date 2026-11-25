using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using VeloxDev.WorkflowSystem;

namespace VeloxDev.WorkflowSystem.AttachedBehaviors;

/// <summary>
/// Blazor analogue of the XAML adapters' <c>WorkflowSlotLayoutBehavior</c> attached property.
/// Measures the on-screen centers of the wrapped slot elements (which expose <c>data-slot-id</c>)
/// and writes them back to each slot's <see cref="ISlotComponent.Anchor"/> in canvas
/// (world) coordinates.
/// </summary>
/// <remarks>
/// The DOM walk and the re-measure schedule stay in JavaScript: <c>initSlotLayout</c> owns the
/// rAF/ResizeObserver orchestration and sends one batch of measured canvas-local centers. The
/// synchronous <see cref="ISlotPartLocator"/> half cannot cross the interop boundary, so this
/// component feeds those centers back through a cached locator and lets
/// <see cref="SlotLayoutPipelineCore.Measure"/> apply the shared write-back rules — duplicate
/// suppression, the sub-pixel tolerance, and the layer-preserving anchor.
/// </remarks>
public partial class WorkflowSlotLayoutBehavior : ComponentBase, IAsyncDisposable
{
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    /// <summary>Gets or sets the node whose slots should be synchronized.</summary>
    [Parameter]
    public INodeComponent? Node { get; set; }

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

    // 测量时机归 JS；这里只把最近一批读数喂给 Core，由它套用共享的写回规则。
    private SlotLayoutPipelineCore? _pipeline;
    private INodeComponent? _pipelineNode;
    private MeasuredSlotLocator? _locator;
    private AnchorWriteHost? _host;

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

    /// <summary>
    /// Receives one batch of measured slot centers from JavaScript and writes the changed anchors
    /// through the shared pipeline.
    /// </summary>
    /// <param name="batch">Each entry is <c>[slotId, canvasLocalX, canvasLocalY]</c>.</param>
    [JSInvokable]
    public void OnSlotLayoutBatch(string[][] batch)
    {
        if (Node is null || !EnsurePipeline())
        {
            return;
        }

        var parts = new List<ISlotPart>(batch.Length);
        foreach (var entry in batch)
        {
            if (entry.Length < 3)
            {
                continue;
            }

            var id = entry[0];
            // 批次来自测量的 JS，用 toFixed() 格式化、总是 '.' 分隔；若按当前文化解析，逗号小数服务器会把 '.' 当千位分隔符，锚点被静默放错 —— 不抛异常、也不是零。
            if (!double.TryParse(entry[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(entry[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
            {
                continue;
            }

            if (!WorkflowRuntimeIds.TryFind<ISlotComponent>(id, out var slot) || slot is null)
            {
                continue;
            }

            parts.Add(new CachedSlotPart(slot, x, y));
        }

        _locator!.SetParts(parts);
        _host!.Wrote = false;
        _pipeline!.Measure();

        // 通知节点其插槽布局变了。消费者按插槽锚点渲染连线，所以这让它们在实时拖拽中立刻重画，而不是等节点自身 Anchor 再变一次。
        if (_host.Wrote)
        {
            Node.OnPropertyChanged(nameof(INodeComponent.Anchor));
        }
    }

    private bool EnsurePipeline()
    {
        if (Node is null)
        {
            StopPipeline();
            return false;
        }

        if (_pipeline is not null && ReferenceEquals(_pipelineNode, Node))
        {
            return true;
        }

        StopPipeline();
        _locator = new MeasuredSlotLocator();
        _host = new AnchorWriteHost();
        _pipelineNode = Node;
        _pipeline = new SlotLayoutPipelineCore(
            Node,
            this,
            _locator,
            _host,
            new SlotLayoutConfiguration(null, MeasuredSlotLocator.EnumeratorName, null, null));
        return true;
    }

    private void StopPipeline()
    {
        _pipeline?.Stop();
        _pipeline = null;
        _pipelineNode = null;
        _locator = null;
        _host = null;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        StopPipeline();

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

    // 把 JS 已算好的画布局部中心伪装成一枚举器的一项；Core 按声明顺序逐项写回，
    // 因此 0.01 阈值、NaN 首写与层号保留都由流水线负责，本层不再复制一遍。
    private sealed class MeasuredSlotLocator : ISlotPartLocator
    {
        public const string EnumeratorName = "__veloxdev_razor_measured_slots__";

        private IReadOnlyList<ISlotPart> _parts = Array.Empty<ISlotPart>();

        public void SetParts(IReadOnlyList<ISlotPart> parts) => _parts = parts;

        public SlotCoordinateSpace CoordinateSpace => SlotCoordinateSpace.CanvasLocal;

        public ISlotPart? FindNamedSlot(string name) => null;

        public IReadOnlyList<ISlotPart> FindEnumeratedSlots(string name)
            => string.Equals(name, EnumeratorName, StringComparison.Ordinal)
                ? _parts
                : Array.Empty<ISlotPart>();

        // 非空哨兵：Core 看到坐标宿存在才走 CanvasLocal 分支；这些中心本来就是画布局部坐标。
        public object? ResolveCoordinateHost(string? hostName, Type? hostType) => this;

        public int CountSlots(IReadOnlyList<string> slotNames, IReadOnlyList<string> enumeratorNames)
            => _parts.Count;
    }

    private sealed class CachedSlotPart : ISlotPart, ISlotPartVisibility
    {
        private readonly ISlotComponent slot;
        private readonly double x;
        private readonly double y;

        public CachedSlotPart(ISlotComponent slot, double x, double y)
        {
            this.slot = slot;
            this.x = x;
            this.y = y;
        }

        public ISlotComponent? Slot => slot;

        public bool IsMeasured => true;

        // JS 只把已排版/已实现的部件送过来；缓存里的部件一定是可见的。
        public bool IsVisible => true;

        public SlotCenter? MeasureCenter(object? relativeTo) => new(x, y);
    }

    private sealed class AnchorWriteHost : ISlotMeasurementHost
    {
        public bool Wrote { get; set; }

        public (double Width, double Height) ReadNodeSize() => (0d, 0d);

        public Anchor ReadAnchor(ISlotComponent slot) => slot.Anchor;

        public void WriteAnchor(ISlotComponent slot, Anchor anchor)
        {
            slot.Anchor = anchor;
            Wrote = true;
        }

        public void InvalidateMeasure()
        {
        }

        public bool Post(Action flush) => false;
    }
}
