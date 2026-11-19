namespace VeloxDev.WorkflowSystem;

/// <summary>
/// The shared implementation of <see cref="IViewHost{THost,TView,TItem,TPriorityCore}"/>:
/// it only groups the platform layers and policies and supplies defaults; every platform-specific
/// decision remains in those layers.
/// </summary>
/// <typeparam name="THost">The platform element that owns the views.</typeparam>
/// <typeparam name="TView">The platform element used as one item's view.</typeparam>
/// <typeparam name="TItem">The item type the pool tracks.</typeparam>
/// <typeparam name="TPriorityCore">The platform's dispatcher-priority type, or <c>NonPriority</c>.</typeparam>
public abstract class ViewHostCore<THost, TView, TItem, TPriorityCore>
    : IViewHost<THost, TView, TItem, TPriorityCore>
    where THost : class
    where TView : class
    where TItem : class
{
    /// <summary>Creates the host from its platform layers and policies.</summary>
    /// <param name="factory">The layer that turns items into views.</param>
    /// <param name="container">The layer that owns visual-tree membership and bound state.</param>
    /// <param name="arranger">The layer that applies item geometry.</param>
    /// <param name="scheduler">The layer that carries batches to the host thread.</param>
    /// <param name="diagnostics">The failure sink; a no-op sink is used when <see langword="null"/>.</param>
    /// <param name="poolPolicy">The policy that bounds batches and recycled views; defaults to 3 per batch, unlimited recycling.</param>
    /// <param name="layerPolicy">The z-order policy; defaults to a constant zero.</param>
    /// <param name="linkLayer">The layer that renders links itself; defaults to "the main pool owns links".</param>
    /// <param name="measureHost">The post-arrange measure hook; defaults to no-op.</param>
    /// <exception cref="ArgumentNullException">A required layer is <see langword="null"/>.</exception>
    protected ViewHostCore(
        IViewFactory<TItem, TView> factory,
        IViewContainer<THost, TView, TItem> container,
        IViewArranger<TItem, TView> arranger,
        IViewScheduler<TPriorityCore> scheduler,
        IViewDiagnostics<TItem>? diagnostics = null,
        IViewPoolPolicy? poolPolicy = null,
        IViewLayerPolicy<TItem>? layerPolicy = null,
        IViewLinkLayer<THost, TItem>? linkLayer = null,
        IViewMeasureHost<THost, TView, TItem>? measureHost = null)
    {
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        Container = container ?? throw new ArgumentNullException(nameof(container));
        Arranger = arranger ?? throw new ArgumentNullException(nameof(arranger));
        Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        Diagnostics = diagnostics ?? NullViewDiagnostics<TItem>.Instance;
        PoolPolicy = poolPolicy ?? DefaultViewPoolPolicy.Instance;
        LayerPolicy = layerPolicy ?? NullViewLayerPolicy<TItem>.Instance;
        LinkLayer = linkLayer ?? NullViewLinkLayer<THost, TItem>.Instance;
        MeasureHost = measureHost ?? NullViewMeasureHost<THost, TView, TItem>.Instance;
    }

    /// <inheritdoc />
    public IViewFactory<TItem, TView> Factory { get; }

    /// <inheritdoc />
    public IViewContainer<THost, TView, TItem> Container { get; }

    /// <inheritdoc />
    public IViewArranger<TItem, TView> Arranger { get; }

    /// <inheritdoc />
    public IViewScheduler<TPriorityCore> Scheduler { get; }

    /// <inheritdoc />
    public IViewDiagnostics<TItem> Diagnostics { get; }

    /// <inheritdoc />
    public IViewPoolPolicy PoolPolicy { get; }

    /// <inheritdoc />
    public IViewLayerPolicy<TItem> LayerPolicy { get; }

    /// <inheritdoc />
    public IViewLinkLayer<THost, TItem> LinkLayer { get; }

    /// <inheritdoc />
    public IViewMeasureHost<THost, TView, TItem> MeasureHost { get; }
}

// 没有接入诊断的宿主用它，保证 Failed 永远有接收方。
internal sealed class NullViewDiagnostics<TItem> : IViewDiagnostics<TItem>
    where TItem : class
{
    public static readonly NullViewDiagnostics<TItem> Instance = new();

    public void Failed(TItem? item, ViewStage stage, Exception? exception)
    {
    }
}

// 默认批策略：与旧 ViewManager 一致，3 个一批，回收不限量。
internal sealed class DefaultViewPoolPolicy : IViewPoolPolicy
{
    public static readonly DefaultViewPoolPolicy Instance = new();

    public int BatchSize => 3;

    public double FrameBudgetMilliseconds => 0d;

    public int MaxRecycledViewsPerType => 0;
}

// 默认层级策略：不写 z 序，交给模板。
internal sealed class NullViewLayerPolicy<TItem> : IViewLayerPolicy<TItem>
    where TItem : class
{
    public static readonly NullViewLayerPolicy<TItem> Instance = new();

    public int GetZOrder(TItem item) => 0;
}

// 默认连线层：主池按普通条目物化每一条连线。
internal sealed class NullViewLinkLayer<THost, TItem> : IViewLinkLayer<THost, TItem>
    where THost : class
    where TItem : class
{
    public static readonly NullViewLinkLayer<THost, TItem> Instance = new();

    public bool IsActive => false;

    public bool Handles(TItem item) => false;

    public void Render(THost host, IReadOnlyList<TItem> items)
    {
    }
}

// 默认测量层：不做事，几何由绑定/布局/浏览器负责。
internal sealed class NullViewMeasureHost<THost, TView, TItem>
    : IViewMeasureHost<THost, TView, TItem>
    where THost : class
    where TView : class
    where TItem : class
{
    public static readonly NullViewMeasureHost<THost, TView, TItem> Instance = new();

    public void Measure(THost host, TView view, TItem item)
    {
    }
}
