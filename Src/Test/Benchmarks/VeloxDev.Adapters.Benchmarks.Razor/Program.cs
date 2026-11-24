using System.Text;
using BenchmarkDotNet.Running;
using Microsoft.AspNetCore.Components.Web;
using VeloxDev.WorkflowSystem;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Adapters.Benchmarks.Razor;

/// <summary>Entry point for the Razor render benchmarks.</summary>
/// <remarks>
/// No arguments runs <see cref="RazorViewPoolRenderBenchmarks"/>; BenchmarkDotNet arguments (<c>--filter</c>,
/// <c>--list</c>, …) are forwarded. <c>--probe</c> renders once, sweeps the viewport and prints the visible-set
/// and allocation evidence — it exists to prove the framework renderer really drives updates without a browser.
/// </remarks>
public static class Program
{
    /// <summary>Runs a benchmark pass or the render probe.</summary>
    /// <param name="args">Command-line arguments; empty runs the benchmark.</param>
    public static void Main(string[] args)
    {
        if (args is ["--probe"])
        {
            Probe.Run();
            return;
        }

        if (args.Length == 0)
        {
            BenchmarkRunner.Run<RazorViewPoolRenderBenchmarks>();
            return;
        }

        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
    }
}

internal static class Probe
{
    internal static void Run()
    {
        const int steps = RenderScenario.ScrollSteps;

        // Core 单独一趟：第二棵树不挂 renderer，只走 Virtualize。
        // 这一趟把 Core 的份额从「renderer 重渲染」里剥出来 —— 优化落在这里，数字要能单独复核。
        var core = RenderScenario.Create();
        SweepCore(core);
        core.Helper.Viewport = core.Origin;

        var coreStart = ProcessAllocated();
        SweepCore(core);
        var coreBytes = ProcessAllocated() - coreStart;
        var corePass = coreBytes / (double)steps;

        // renderer 那一趟：视口移动 + ViewPool 因集合通知重渲染。
        var scenario = RenderScenario.Create();
        var parameters = scenario.CreateParameters();
        var renderer = RenderScenario.CreateRenderer();
        try
        {
            var root = renderer.Dispatcher
                .InvokeAsync(() => renderer.BeginRenderingComponent<ViewPool>(parameters))
                .GetAwaiter()
                .GetResult();

            // 预热：先把 20 步完整走一遍，再把视口放回原点。
            SweepRenderer(renderer, scenario);
            ResetToOrigin(renderer, scenario);

            var initialItems = renderer.Dispatcher
                .InvokeAsync(() => CountPoolItems(root.ToHtmlString()))
                .GetAwaiter()
                .GetResult();
            var visibleBefore = scenario.Helper.VisibleItems.Count;

            // 单次视口移动：进程级分配（thread-level 视角会漏掉 renderer 所在线程）。
            var oneStart = ProcessAllocated();
            renderer.Dispatcher
                .InvokeAsync(() => scenario.Helper.Viewport = scenario.ScrollViewports[0])
                .GetAwaiter()
                .GetResult();
            Thread.Sleep(80);
            var oneBytes = ProcessAllocated() - oneStart;

            var updatedItems = renderer.Dispatcher
                .InvokeAsync(() => CountPoolItems(root.ToHtmlString()))
                .GetAwaiter()
                .GetResult();
            var visibleAfter = scenario.Helper.VisibleItems.Count;

            // 20 步对角扫：先回原点，再整趟测量。
            ResetToOrigin(renderer, scenario);
            var sweepStart = ProcessAllocated();
            SweepRenderer(renderer, scenario);
            var sweepBytes = ProcessAllocated() - sweepStart;
            var sweepPass = sweepBytes / (double)steps;

            // 可见集内容与顺序的指纹：单独一趟（指纹本身会分配，不能进测量窗口）。
            var (counts, orderHash) = FingerprintRenderer(renderer, scenario);
            var (coreCounts, coreOrderHash) = FingerprintCore(core);

            Console.WriteLine($"thread        : {Environment.CurrentManagedThreadId}");
            Console.WriteLine($"visible items : {visibleBefore} -> {visibleAfter}");
            Console.WriteLine($"pool items    : {initialItems} -> {updatedItems}");
            Console.WriteLine($"alloc one move: {oneBytes} B process-wide");
            Console.WriteLine($"core sweep    : {coreBytes} B process-wide for {steps} passes ({corePass:F0} B/pass, no renderer)");
            Console.WriteLine($"sweep         : {sweepBytes} B process-wide for {steps} passes ({sweepPass:F0} B/pass, with renderer)");
            Console.WriteLine($"core visible  : counts=[{string.Join(',', coreCounts)}] orderHash=0x{coreOrderHash:X8}");
            Console.WriteLine($"rend visible  : counts=[{string.Join(',', counts)}] orderHash=0x{orderHash:X8}");
            Console.WriteLine(updatedItems > 0 && visibleAfter > 0
                ? "PROBE: the renderer re-rendered the pool without a browser"
                : "PROBE: empty render");
        }
        finally
        {
            renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    // 进程级口径：thread-level 的 GC.GetAllocatedBytesForCurrentThread 看不到 renderer/tick 线程的分配。
    private static long ProcessAllocated() => GC.GetTotalAllocatedBytes(precise: true);

    private static void SweepCore(RenderScenario scenario)
    {
        foreach (var viewport in scenario.ScrollViewports)
            scenario.Helper.Viewport = viewport;
    }

    private static void SweepRenderer(HtmlRenderer renderer, RenderScenario scenario)
    {
        foreach (var viewport in scenario.ScrollViewports)
        {
            renderer.Dispatcher
                .InvokeAsync(() => scenario.Helper.Viewport = viewport)
                .GetAwaiter()
                .GetResult();

            // 给 ViewPool 的合并重渲染留一次调度：否则 20 步会被合并成一次，renderer 的份额被吞掉。
            Thread.Sleep(5);
        }
    }

    private static void ResetToOrigin(HtmlRenderer renderer, RenderScenario scenario)
    {
        renderer.Dispatcher
            .InvokeAsync(() => scenario.Helper.Viewport = scenario.Origin)
            .GetAwaiter()
            .GetResult();
        Thread.Sleep(80);
    }

    private static (int[] Counts, uint OrderHash) FingerprintRenderer(HtmlRenderer renderer, RenderScenario scenario)
    {
        ResetToOrigin(renderer, scenario);
        return FingerprintPass(
            scenario,
            viewport => renderer.Dispatcher
                .InvokeAsync(() => scenario.Helper.Viewport = viewport)
                .GetAwaiter()
                .GetResult());
    }

    private static (int[] Counts, uint OrderHash) FingerprintCore(RenderScenario scenario)
    {
        scenario.Helper.Viewport = scenario.Origin;
        return FingerprintPass(scenario, viewport => scenario.Helper.Viewport = viewport);
    }

    // 每一步记下可见集及其顺序；orderHash 把每一步的内容指纹串起来，前后两次跑必须逐位相同。
    private static (int[] Counts, uint OrderHash) FingerprintPass(RenderScenario scenario, Action<Viewport> move)
    {
        var ids = BuildIdMap(scenario);
        var items = scenario.Helper.VisibleItems;
        var counts = new int[scenario.ScrollViewports.Count];
        var orderHash = 2166136261u;
        var idsBuilder = new StringBuilder();

        for (var step = 0; step < scenario.ScrollViewports.Count; step++)
        {
            move(scenario.ScrollViewports[step]);
            Thread.Sleep(5);

            counts[step] = items.Count;
            var stepHash = 2166136261u;
            for (var index = 0; index < items.Count; index++)
            {
                var id = ids.TryGetValue(items[index], out var value) ? value : -2;
                stepHash = (stepHash ^ (uint)id) * 16777619u;
                if (step == 0)
                {
                    if (index > 0) idsBuilder.Append(',');
                    idsBuilder.Append(id);
                }
            }

            orderHash = (orderHash ^ stepHash) * 16777619u;
        }

        if (counts.Length > 0)
            Console.WriteLine($"visible[step0]: count={counts[0]} ids=[{idsBuilder}]");
        return (counts, orderHash);
    }

    private static Dictionary<object, int> BuildIdMap(RenderScenario scenario)
    {
        var ids = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        for (var index = 0; index < scenario.Tree.Nodes.Count; index++)
            ids[scenario.Tree.Nodes[index]] = index;
        for (var index = 0; index < scenario.Tree.Links.Count; index++)
            ids[scenario.Tree.Links[index]] = 100000 + index;
        ids[scenario.Tree.VirtualLink] = -1;
        return ids;
    }

    private static int CountPoolItems(string html)
    {
        const string marker = "veloxdev-wf-pool-item";
        var count = 0;
        for (var index = 0; (index = html.IndexOf(marker, index, StringComparison.Ordinal)) >= 0; index += marker.Length)
        {
            count++;
        }

        return count;
    }
}
