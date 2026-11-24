using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging.Abstractions;
using VeloxDev.Adapters.Benchmarks.Headed;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Adapters.Benchmarks.Razor;

// 与有头基准同参数的一条场景：2000 节点 / 3000 条本地连线、1200×800 视口。
// VisibleItems 由 Core 的空间虚拟化真实产出，不是手工塞进去的列表。
internal sealed class RenderScenario
{
    // 对角扫视口的步数；[Benchmark] 的 OperationsPerInvoke 也用它，所以必须是常量。
    internal const int ScrollSteps = 20;

    internal const double ViewportWidth = 1200d;

    internal const double ViewportHeight = 800d;

    private RenderScenario(TreeDefaultViewModel tree, Viewport[] scrollViewports)
    {
        Tree = tree;
        ScrollViewports = scrollViewports;
    }

    internal TreeDefaultViewModel Tree { get; }

    internal ITreeHelper Helper => Tree.GetHelper();

    internal Viewport Origin => new(0d, 0d, ViewportWidth, ViewportHeight);

    internal IReadOnlyList<Viewport> ScrollViewports { get; }

    internal static RenderScenario Create()
    {
        var options = new HeadedOptions
        {
            Nodes = 2000,
            Links = 3000,
            ViewportWidth = ViewportWidth,
            ViewportHeight = ViewportHeight,
        };
        var synthetic = SyntheticWorkflow.Create(options);
        var helper = synthetic.Tree.GetHelper();

        // 这一步会把空间索引的脏标记消费掉；helper 的 10fps tick 随后会拿同一个视口再虚拟化一次。
        helper.Viewport = new Viewport(0d, 0d, ViewportWidth, ViewportHeight);

        // 等 tick 把 Create 期间留下的脏标记跑完，测量中才不会有另一条线程同时改 VisibleItems。
        Thread.Sleep(300);

        if (helper.VisibleItems.Count <= 1)
        {
            throw new InvalidOperationException(
                "Spatial virtualization produced no visible items; the render benchmark would measure an empty pool.");
        }

        var maxX = Math.Max(0d, synthetic.ContentWidth - ViewportWidth);
        var maxY = Math.Max(0d, synthetic.ContentHeight - ViewportHeight);
        var viewports = new Viewport[ScrollSteps];
        for (var index = 0; index < ScrollSteps; index++)
        {
            // 从第一格开始（而不是原点），每一步都改变可见集；20 步走到内容右下角。
            var t = (index + 1d) / ScrollSteps;
            viewports[index] = new Viewport(maxX * t, maxY * t, ViewportWidth, ViewportHeight);
        }

        return new RenderScenario(synthetic.Tree, viewports);
    }

    internal static HtmlRenderer CreateRenderer()
        => new(EmptyServiceProvider.Instance, NullLoggerFactory.Instance);

    internal ParameterView CreateParameters()
        => ParameterView.FromDictionary(new Dictionary<string, object?>
        {
            [nameof(ViewPool.ItemsSource)] = Helper.VisibleItems,
            [nameof(ViewPool.ItemTemplate)] = ViewPoolTemplate.Item,
            [nameof(ViewPool.KeySelector)] = ViewPoolTemplate.KeySelector,
        });

    // HtmlRenderer 只拿它解析可选服务（组件激活器等）；缺席时框架走默认实现。
    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
