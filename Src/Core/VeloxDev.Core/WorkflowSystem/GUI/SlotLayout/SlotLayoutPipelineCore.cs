namespace VeloxDev.WorkflowSystem;

/// <summary>
/// Owns the platform-independent half of slot measurement: which parts of a node view to measure, the order anchors
/// are written back, duplicate suppression, and the re-measure schedule shared by the node's canvas.
/// </summary>
/// <remarks>
/// <para>
/// An adapter supplies an <see cref="ISlotPartLocator"/> for its visual tree and an
/// <see cref="ISlotMeasurementHost"/> for the model and UI thread. This type never names a platform type: it walks
/// the configured names in order, asks the locator for each part, converts the measured center through
/// <see cref="WorkflowSurfaceMath"/>, and writes only anchors that actually changed.
/// </para>
/// <para>
/// Re-measure requests go through the shared <see cref="WorkflowLayoutBatch"/> keyed by the node view: a
/// <see cref="SlotLayoutTrigger.Layout"/> request compares a <see cref="WorkflowLayoutStamp"/> and flushes inside the
/// layout pass, while every other trigger schedules one UI-thread hop. The stamp is certified only after a pass that
/// found every part the configuration expects; a pass that found nothing (containers not realized yet) is retried by
/// the next layout pass instead of being suppressed. <see cref="Stop"/> makes any request already queued a no-op, so
/// an adapter can dispose this pipeline from its detach path without racing the platform queue.
/// </para>
/// </remarks>
public sealed class SlotLayoutPipelineCore
{
    // 0.01 是各适配器沿用至今的「同一锚点」阈值：亚像素抖动不算变化，避免 setter → 通知 → 重测的回环。
    private const double AnchorTolerance = 0.01d;

    private readonly ISlotPartLocator _locator;
    private readonly ISlotMeasurementHost _host;
    private readonly object _view;
    private readonly Action _work;
    private WorkflowLayoutBatch? _batch;
    private object? _batchOwner;
    private bool _active = true;
    private bool _measuring;

    /// <summary>Creates a pipeline for one node view.</summary>
    /// <param name="node">The node whose slots are measured; its parent tree scopes the shared batch and supplies the layout.</param>
    /// <param name="view">The platform element that represents the node; used as the batch key.</param>
    /// <param name="locator">The platform visual-tree walk and coordinate-host resolver.</param>
    /// <param name="host">The platform model and UI-thread access.</param>
    /// <param name="configuration">The parts to measure and the coordinate host to measure against.</param>
    /// <exception cref="ArgumentNullException">Any argument is <see langword="null"/>.</exception>
    public SlotLayoutPipelineCore(
        INodeComponent node,
        object view,
        ISlotPartLocator locator,
        ISlotMeasurementHost host,
        SlotLayoutConfiguration configuration)
    {
        Node = node ?? throw new ArgumentNullException(nameof(node));
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _host = host ?? throw new ArgumentNullException(nameof(host));
        Configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _work = RunQueued;
        WatchedPropertyNames = BuildWatchedPropertyNames(configuration);
    }

    /// <summary>The node whose slots this pipeline measures.</summary>
    public INodeComponent Node { get; }

    /// <summary>The parts and coordinate host this pipeline was created with.</summary>
    public SlotLayoutConfiguration Configuration { get; }

    /// <summary>
    /// The model property names whose change requires a re-measure: node geometry, every configured part name with
    /// its <c>PART_</c> prefix stripped, and the standard slot property names.
    /// </summary>
    /// <remarks>
    /// An attached behavior subscribes to one <see cref="System.ComponentModel.INotifyPropertyChanged"/> source
    /// (the node) and drops every notification outside this set, so a title or colour change does not walk the tree.
    /// </remarks>
    public IReadOnlyCollection<string> WatchedPropertyNames { get; }

    /// <summary>Requests a measure from a platform notification.</summary>
    /// <param name="trigger">The notification that raised the request.</param>
    /// <remarks>
    /// <see cref="SlotLayoutTrigger.Layout"/> captures the stamp and flushes synchronously when the request is
    /// queued; every other trigger queues unconditionally and schedules one deferred flush. A layout pass certifies
    /// the stamp only when it <b>found</b> every part the configuration expects and no visible part was still waiting
    /// for layout: such a pass leaves the stamp uncertified, so the next layout pass retries instead of the unchanged
    /// stamp suppressing the retry and leaving the anchors at their <c>NaN</c> default. A <b>hidden</b> part
    /// (<see cref="ISlotPartVisibility.IsVisible"/> is <see langword="false"/>) is a steady state and still certifies
    /// the pass — otherwise it would re-measure on every layout pass forever. A part that does not implement
    /// <see cref="ISlotPartVisibility"/> is treated as visible, so existing implementers keep compiling.
    /// </remarks>
    public void RequestMeasure(SlotLayoutTrigger trigger)
    {
        if (!_active)
        {
            return;
        }

        var batch = Batch();

        if (trigger == SlotLayoutTrigger.Layout)
        {
            var (width, height) = _host.ReadNodeSize();
            int expected;
            var controlIdentity = 0;
            if (_locator is ISlotPartIdentityLocator identityLocator)
            {
                controlIdentity = identityLocator.ComputeSlotIdentity(
                    Configuration.SlotNames, Configuration.EnumeratorNames, out expected);
            }
            else
            {
                expected = _locator.CountSlots(Configuration.SlotNames, Configuration.EnumeratorNames);
            }

            var slotSignature = unchecked(ComputeSlotSignature() * 397 ^ controlIdentity);
            var stamp = WorkflowLayoutStamp.Capture(Node, width, height, expected, slotSignature);
            if (batch.Request(_view, stamp, () => MeasureParts(expected)))
            {
                batch.Flush();
            }

            return;
        }

        if (trigger == SlotLayoutTrigger.Configuration)
        {
            _host.InvalidateMeasure();
        }

        batch.Request(_view, _work);
        batch.Schedule();
    }

    /// <summary>Measures every configured part immediately on the calling thread.</summary>
    /// <remarks>
    /// Parts are visited in declaration order — single slots first, then each enumerator's realized items — so the
    /// write-back order is deterministic. A part whose anchor did not change is not written. Re-entrant calls (a
    /// layout pass raised from inside a write) return immediately.
    /// </remarks>
    public void Measure() => MeasureParts(int.MaxValue);

    // 返回值是「这一趟是否看全了」：expected 是配置里该看见的部件数（locator 的数法），
    // 容器还没实例化时 found 会小于它，这一趟就不能当作「输入没变」的证据。
    // 只看 found、不看元素是否已排版：一个永远不显示的部件（隐藏的端口）不能变成每趟布局都重测的忙循环。
    private bool MeasureParts(int expected)
    {
        if (!_active || _measuring)
        {
            return false;
        }

        _measuring = true;
        try
        {
            var coordinateHost = _locator.ResolveCoordinateHost(
                Configuration.CoordinateHostName, Configuration.CoordinateHostType);

            var found = 0;
            var visibleUnmeasured = false;

            foreach (var name in Configuration.SlotNames)
            {
                if (_locator.FindNamedSlot(name) is { } part)
                {
                    found++;
                    visibleUnmeasured |= IsVisibleButUnmeasured(part);
                    MeasurePart(part, coordinateHost);
                }
            }

            foreach (var name in Configuration.EnumeratorNames)
            {
                var parts = _locator.FindEnumeratedSlots(name);
                for (var i = 0; i < parts.Count; i++)
                {
                    found++;
                    visibleUnmeasured |= IsVisibleButUnmeasured(parts[i]);
                    MeasurePart(parts[i], coordinateHost);
                }
            }

            return found >= expected && !visibleUnmeasured;
        }
        finally
        {
            _measuring = false;
        }
    }

    /// <summary>Drops any queued measure without stopping the pipeline; the next request schedules again.</summary>
    /// <remarks>An adapter calls this when a view temporarily leaves the visual tree but may come back unchanged.</remarks>
    public void CancelPending() => _batch?.Cancel(_view);

    /// <summary>Stops measuring; any work already queued for <see cref="Measure"/> becomes a no-op.</summary>
    public void Stop()
    {
        if (!_active)
        {
            return;
        }

        _active = false;
        _batch?.Cancel(_view);
    }

    // 可见但尚未排版的部件是瞬态：这一趟不能认证指纹，否则锚点停在 NaN，连线被渲染门隐藏到下
    // 一次几何变化。隐藏端口（可选接口报 IsVisible=false）是稳态，可以认证，否则每趟布局都会重测。
    // 未实现可选接口的部件按可见处理，保持向后兼容（旧实现者不改代码也能编译，锚点会照常重试）。
    private static bool IsVisibleButUnmeasured(ISlotPart part)
        => part.Slot is not null
           && !part.IsMeasured
           && (part is not ISlotPartVisibility visibility || visibility.IsVisible);

    private void MeasurePart(ISlotPart part, object? coordinateHost)
    {
        var slot = part.Slot;
        if (slot is null || !part.IsMeasured)
        {
            return;
        }

        if (coordinateHost is not null && part.MeasureCenter(coordinateHost) is { } center)
        {
            var layout = Node.Parent?.Layout;
            Anchor anchor;
            switch (_locator.CoordinateSpace)
            {
                case SlotCoordinateSpace.CanvasLocal:
                    anchor = WorkflowSurfaceMath.SlotAnchorFromCanvasLocal(center.X, center.Y, slot.Anchor.Layer);
                    break;
                case SlotCoordinateSpace.ScrollSpace when layout is not null:
                    anchor = WorkflowSurfaceMath.SlotAnchorFromVisualCenter(
                        center.X, center.Y, slot.Anchor.Layer, layout);
                    break;
                default:
                    // 坐标宿存在但拿不到布局（或适配器声明的是 NodeLocal）：按节点局部空间重测。
                    WriteFromNode(part, slot);
                    return;
            }

            WriteIfChanged(slot, anchor);
            return;
        }

        WriteFromNode(part, slot);
    }

    private void WriteFromNode(ISlotPart part, ISlotComponent slot)
    {
        if (part.MeasureCenter(null) is not { } local)
        {
            return;
        }

        var anchor = WorkflowSurfaceMath.SlotAnchorFromNode(
            Node.Anchor.Horizontal, Node.Anchor.Vertical, local.X, local.Y, slot.Anchor.Layer);
        WriteIfChanged(slot, anchor);
    }

    private void WriteIfChanged(ISlotComponent slot, Anchor anchor)
    {
        var current = _host.ReadAnchor(slot);
        // NaN 与任何值都不相等：未初始化过的锚点必须写入，不能因为 Math.Abs(NaN - x) 的假值被当成「没变」。
        if (!double.IsNaN(current.Horizontal)
            && !double.IsNaN(current.Vertical)
            && Math.Abs(current.Horizontal - anchor.Horizontal) <= AnchorTolerance
            && Math.Abs(current.Vertical - anchor.Vertical) <= AnchorTolerance
            && current.Layer == anchor.Layer)
        {
            return;
        }

        _host.WriteAnchor(slot, anchor);
    }

    private WorkflowLayoutBatch Batch()
    {
        var owner = Node.Parent ?? _view;
        if (_batch is not null && ReferenceEquals(owner, _batchOwner))
        {
            return _batch;
        }

        // 节点从无树变成有树（或换树）时批次作用域要跟着换，否则同帧请求会落在两个队列里。
        _batch?.Cancel(_view);
        _batchOwner = owner;
        _batch = WorkflowLayoutBatch.For(owner, _host.Post);
        return _batch;
    }

    private void RunQueued()
    {
        if (_active)
        {
            Measure();
        }
    }

    // 槽位实例集的指纹：选择器切换会换一套槽位，数量可能不变；只看数量会让新槽位停在 NaN，
    // 连线被隐藏到下一次几何变化（拖动）为止。按引用身份哈希，无分配。平台容器身份由
    // ISlotPartIdentityLocator 单独提供，覆盖「同一模型槽位、容器重建」的情况。
    private int ComputeSlotSignature()
    {
        var slots = Node.Slots;
        if (slots is null)
        {
            return 0;
        }

        var hash = 17;
        for (var index = 0; index < slots.Count; index++)
        {
            hash = unchecked(hash * 31 + System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(slots[index]));
        }

        return hash;
    }

    private static IReadOnlyCollection<string> BuildWatchedPropertyNames(SlotLayoutConfiguration configuration)
    {
        var names = new HashSet<string>(StringComparer.Ordinal)
        {
            nameof(INodeComponent.Anchor),
            nameof(INodeComponent.Size),
        };

        foreach (var name in configuration.SlotNames)
        {
            AddPartNames(names, name);
        }

        foreach (var name in configuration.EnumeratorNames)
        {
            AddPartNames(names, name);
        }

        // View-model 属性名（OutputSlots）与控件名（PART_OutputSlots）不同；标准名兜底覆盖直接绑定。
        names.Add("InputSlot");
        names.Add("OutputSlot");
        names.Add("OutputSlots");
        return names;
    }

    private static void AddPartNames(HashSet<string> names, string part)
    {
        names.Add(part);
        if (part.StartsWith("PART_", StringComparison.Ordinal))
        {
            names.Add(part.Substring(5));
        }
    }
}
