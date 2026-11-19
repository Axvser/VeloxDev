using BenchmarkDotNet.Running;
using Microsoft.AspNetCore.Components.Web;
using VeloxDev.WorkflowSystem.AttachedBehaviors;

namespace VeloxDev.Adapters.Benchmarks.Razor;

/// <summary>Entry point for the Razor render benchmarks.</summary>
/// <remarks>
/// No arguments runs <see cref="RazorViewPoolRenderBenchmarks"/>; BenchmarkDotNet arguments (<c>--filter</c>,
/// <c>--list</c>, …) are forwarded. <c>--probe</c> renders once, moves the viewport once and prints the visible-set
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
        var scenario = RenderScenario.Create();
        var parameters = scenario.CreateParameters();
        var renderer = RenderScenario.CreateRenderer();
        try
        {
            var root = renderer.Dispatcher
                .InvokeAsync(() => renderer.BeginRenderingComponent<ViewPool>(parameters))
                .GetAwaiter()
                .GetResult();

            var initialItems = renderer.Dispatcher
                .InvokeAsync(() => CountPoolItems(root.ToHtmlString()))
                .GetAwaiter()
                .GetResult();
            var visibleBefore = scenario.Helper.VisibleItems.Count;

            // 只量这一段：视口移动 + ViewPool 因集合通知而再次渲染。
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var next = scenario.ScrollViewports[0];
            renderer.Dispatcher
                .InvokeAsync(() => scenario.Helper.Viewport = next)
                .GetAwaiter()
                .GetResult();
            var allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

            var updatedItems = renderer.Dispatcher
                .InvokeAsync(() => CountPoolItems(root.ToHtmlString()))
                .GetAwaiter()
                .GetResult();
            var visibleAfter = scenario.Helper.VisibleItems.Count;

            Console.WriteLine($"thread        : {Environment.CurrentManagedThreadId}");
            Console.WriteLine($"visible items : {visibleBefore} -> {visibleAfter}");
            Console.WriteLine($"pool items    : {initialItems} -> {updatedItems}");
            Console.WriteLine($"allocated     : {allocatedAfter - allocatedBefore} B on the calling thread for one viewport move");
            Console.WriteLine(updatedItems > 0 && visibleAfter > 0
                ? "PROBE: the renderer re-rendered the pool without a browser"
                : "PROBE: empty render");
        }
        finally
        {
            renderer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
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
